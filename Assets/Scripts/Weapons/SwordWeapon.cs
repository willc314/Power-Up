using UnityEngine;

/// <summary>
/// Primary melee weapon: spawns a large sword in front of the hero that
/// sweeps across an arc. Alternates between left-to-right and right-to-left
/// swings each time it fires, so a quick double-tap looks like a 1-2 combo.
/// </summary>
public class SwordWeapon : Weapon
{
    [Header("Sword")]
    [Tooltip("Prefab spawned for each swing. Should have a SwordSlash component on its root, ideally wrapping a sword model (e.g. OHS03Polyart).")]
    public SwordSlash slashPrefab;
    [Tooltip("How far in front of the hero the swing centers.")]
    public float spawnDistance = 1.0f;
    [Tooltip("Vertical offset above the hero's pivot so the swing reads at chest/shoulder height.")]
    public float spawnHeight = 1.0f;

    [Header("Boost Tuning — Slash Arc (Damage boost)")]
    [Tooltip("Total degrees added to the slash's arc + hitArc from Sword pickups (Damage boost). Persists across swings.")]
    public float arcDegreesBonus = 0f;
    [Tooltip("Degrees of arc each Sword/Bow/Shield pickup adds to the slash, until it reaches maxSlashArcDegrees.")]
    public float arcDegreesPerBoost = 50f;
    [Tooltip("Maximum total arc the slash can sweep (visual + bonus). 360 = full circle around the hero.")]
    public float maxSlashArcDegrees = 360f;

    [Header("Boost Tuning — Blade Size (rides on the arc boost)")]
    [Tooltip("Multiplier on the slash's blade size and reach. Each arc-growing Damage boost scales this up so the sword physically grows with the player.")]
    public float bladeSizeMultiplier = 1f;
    [Tooltip("Fraction added to bladeSizeMultiplier per Damage boost while the arc is still growing. 0.10 = +10% per pickup.")]
    public float bladeSizeIncreasePerBoost = 0.10f;
    [Tooltip("Cap on bladeSizeMultiplier so the blade doesn't get absurdly large.")]
    public float maxBladeSizeMultiplier = 2.5f;

    [Header("Zenith (level-up unlock + stat-gated auto-activation)")]
    [Tooltip("True once the player has CHOSEN the Zenith upgrade at a level-up. Allows dual-wielding the sword (PowerUpChoiceUI's duplicate-weapon guard relaxes for sword pickups) and arms the auto-activation check.")]
    public bool zenithUnlocked = false;
    [Tooltip("True once Zenith has actually been APPLIED — auto-set when the four threshold conditions are all satisfied at the same time. Drives the elliptical swing path, rainbow trail, and doubled base damage.")]
    public bool zenithApplied = false;
    [Tooltip("Damage multiplier applied to the sword's base damage when Zenith activates. 2 = doubles damage permanently.")]
    public float zenithDamageMultiplier = 2f;
    [Tooltip("Required extra-projectile (extra-slash) count to activate Zenith. Strictly GREATER THAN this value, so the default 3 means the player must have extraAttackCount of 4+.")]
    public int zenithExtraAttacksRequired = 3;
    [Tooltip("Required minimum cooldown-reduction fraction to activate Zenith. 0.30 = current cooldown must be at most 70% of the original (i.e. >30% attack-speed buff).")]
    [Range(0f, 0.95f)] public float zenithAttackSpeedReductionRequired = 0.30f;

    /// <summary>
    /// True after the player has defeated at least one boss (SlimeKing or
    /// SlimeGod) AFTER taking the Zenith upgrade. Required gate for
    /// activation alongside the four stat thresholds. Set externally by
    /// the GameManager.OnAnyBossKilled subscriber below.
    /// </summary>
    [System.NonSerialized] public bool zenithBossDefeatedSinceCurse = false;
    [Tooltip("Minor (sides) semi-axis of the elliptical sweep, in world units. Larger = wider arc on the sides; smaller = thinner / more spear-like.")]
    public float zenithSemiMinor = 1.8f;
    [Tooltip("Per-swing random jitter applied to zenithSemiMinor. The actual minor axis for a given swing is zenithSemiMinor ± Random.Range(-jitter, +jitter), so consecutive swings vary in width slightly. Set to 0 for no variation.")]
    public float zenithSemiMinorJitter = 0.35f;
    [Tooltip("How aggressively the swing's lateral width grows with cursor distance. The actual minor axis is multiplied by Mathf.Max(1, cursorDist / prefab.radius)^widenStrength — so 0 = no widening (constant arc width regardless of cursor), 1 = linear scaling, 2 = quadratic (very dramatic widening for far cursors).")]
    public float zenithSemiMinorWidenStrength = 1.0f;
    [Tooltip("Distance (world units) from the slash's pivot to the BLADE'S MIDDLE along the swing's outward direction. Subtracted from cursor distance so the blade's middle (not the pivot) lands exactly on the cursor at the swing apex. Match this to roughly half the bladeLength for a centered feel.")]
    public float zenithBladeMidOffset = 2.1f;
    [Tooltip("Alpha applied to the per-swing rainbow trail tint when Zenith is active. The Trail's material override picks up the tint's RGB AND alpha — low values keep the rainbow streaks faint and ghostly so they don't blow out the screen. Set to 1 for fully opaque rainbow trails.")]
    [Range(0f, 1f)] public float zenithTrailAlpha = 0.098039f; // 25/255
    [Tooltip("Per-swing trail duration override applied when Zenith is active. Lower values = shorter, snappier streaks that fade out faster behind the blade. Default 0.1s.")]
    public float zenithTrailDuration = 0.1f;
    [Tooltip("Per-swing trail corner-smoothness override applied when Zenith is active. Higher = smoother round corners on the trail's mesh. Default 16. Note: high values increase trail mesh complexity.")]
    public int zenithTrailCorner = 16;

    /// <summary>
    /// True when this single SwordWeapon component is being dual-wielded —
    /// i.e. Zenith was applied and the sword now occupies BOTH primary and
    /// secondary slots. Hero.DispatchWeaponInput uses this to route the
    /// secondary slot's RMB through TryFireSecondary so each slot has its
    /// own independent cooldown.
    /// </summary>
    public bool IsZenithDualWield => zenithApplied;

    // Toggles each fire so successive swings come from opposite sides.
    private bool nextSwingRightToLeft = false;

    // Per-slot cooldown for Zenith dual-wield. The base Weapon class's
    // shared cooldownTimer covers the PRIMARY slot; this one covers the
    // SECONDARY slot when both slots reference the same SwordWeapon.
    // Always ticks (cheap) but only consumed by TryFireSecondary, which
    // itself only fires when zenithApplied is true.
    private float secondaryCooldownTimer = 0f;
    public float SecondaryCooldownProgress => cooldown > 0f ? Mathf.Clamp01(secondaryCooldownTimer / cooldown) : 0f;
    public bool  CanFireSecondary => secondaryCooldownTimer <= 0f;

    // Returns the slash's effective arc with the current bonus applied.
    private float CurrentSlashArc()
    {
        float baseArc = slashPrefab != null ? slashPrefab.arcDegrees : 140f;
        return baseArc + arcDegreesBonus;
    }

    private bool IsArcMaxed()
    {
        return CurrentSlashArc() >= maxSlashArcDegrees - 0.001f;
    }

    /// <summary>
    /// Public surface for the Zenith activation gate. Returns true when the
    /// slash arc is at its 360° cap regardless of which pickup type is
    /// currently in the powerup choice UI. The private overload checks the
    /// same condition but lives behind PickupIsSword-specific logic.
    /// </summary>
    public bool IsSlashArcAtMax => IsArcMaxed();

    // The arc/blade-size growth path is reserved for SWORD pickups. Bow and
    // Shield pickups also map to BoostKind.Damage — those should fall back
    // to the regular +damage behavior on the base class so the Sword still
    // benefits from non-sword Damage pickups in the conventional way.
    private static bool PickupIsSword()
    {
        return PowerUpChoiceUI.LastPickupType == eWeaponType.sword;
    }

    // Crossbow pickup → Projectiles boost. On a Sword, this chains extra
    // slashes spread across the cooldown (the alternating-arc logic in
    // Fire() makes those extras swing from the opposite side automatically).
    //
    // Sword/Shield/Bow pickup → Damage boost. On the Sword this widens the
    // slash arc by arcDegreesPerBoost (default 50°) per pickup, capped at
    // maxSlashArcDegrees (default 360 = full circle around the hero). Past
    // the cap, falls back to the base damage boost (which scales naturally
    // via post-max scaling once damageLevel is also capped).
    public override bool IsBoostMaxed(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles) return extraAttackCount >= maxExtraAttackCount;
        // Damage from a Sword pickup → arc growth; we're maxed only when both
        // the arc AND the damage path are full. Damage from a Bow or Shield
        // pickup falls through to the base class's vanilla damage rules.
        if (kind == BoostKind.Damage && PickupIsSword()) return IsArcMaxed() && IsDamageMaxed;
        return base.IsBoostMaxed(kind);
    }

    public override string DescribeBoost(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles)
        {
            if (IsBoostMaxed(BoostKind.Projectiles))
                return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
            return "+1 Extra Slash";
        }
        if (kind == BoostKind.Damage && PickupIsSword())
        {
            if (IsArcMaxed())
            {
                // Arc maxed → fall back to the base scaled-damage description.
                return base.DescribeBoost(kind);
            }
            // Show the actual delta the arc will gain (might be < 50 on the
            // last step before cap), and the matching blade-size growth.
            float remainingArc = Mathf.Min(arcDegreesPerBoost, maxSlashArcDegrees - CurrentSlashArc());
            bool sizeFull = bladeSizeMultiplier >= maxBladeSizeMultiplier - 0.001f;
            if (sizeFull) return $"+{remainingArc:0}° Slash Arc";
            return $"+{remainingArc:0}° Slash Arc  •  +{bladeSizeIncreasePerBoost * 100f:0}% Blade Size";
        }
        return base.DescribeBoost(kind);
    }

    public override string GetExtraStatsBlock()
    {
        // Show the upgrade-driven values that the player has banked.
        float arc = CurrentSlashArc();
        var sb = new System.Text.StringBuilder();
        sb.Append($"Slash Arc:  {arc:0}°");
        if (bladeSizeMultiplier > 1.0001f)   sb.Append($"\nBlade Size: {bladeSizeMultiplier:0.##}×");
        if (extraAttackCount > 0)            sb.Append($"\nExtra Slashes: {extraAttackCount}");
        return sb.ToString();
    }

    public override bool TryApplyBoost(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles)
        {
            if (IsBoostMaxed(BoostKind.Projectiles))
            {
                damage     += damageIncreasePerLevel * postMaxBoostScale;
                damageLevel++;
                return true;
            }
            extraAttackCount++;
            return true;
        }
        if (kind == BoostKind.Damage && PickupIsSword())
        {
            if (!IsArcMaxed())
            {
                // Grow the arc (clamped at the cap)…
                float baseArc = slashPrefab != null ? slashPrefab.arcDegrees : 140f;
                float newTotal = Mathf.Min(maxSlashArcDegrees, CurrentSlashArc() + arcDegreesPerBoost);
                arcDegreesBonus = newTotal - baseArc;
                // …and grow the blade size + reach alongside it (capped).
                bladeSizeMultiplier = Mathf.Min(maxBladeSizeMultiplier,
                    bladeSizeMultiplier + bladeSizeIncreasePerBoost);
                return true;
            }
            // Arc capped — defer to base for damage handling (with post-max
            // scaling once damageLevel is also at cap).
            return base.TryApplyBoost(kind);
        }
        return base.TryApplyBoost(kind);
    }

    private void OnEnable()  { GameManager.OnAnyBossKilled += HandleBossKilled; }
    private void OnDisable() { GameManager.OnAnyBossKilled -= HandleBossKilled; }

    /// <summary>
    /// Invoked whenever the GameManager reports a boss death. If the
    /// curse is active (zenithUnlocked && !zenithApplied), flip the
    /// boss-defeat gate so MeetsZenithRequirements can pass.
    /// </summary>
    private void HandleBossKilled()
    {
        if (zenithUnlocked && !zenithApplied)
            zenithBossDefeatedSinceCurse = true;
    }

    protected override void Update()
    {
        base.Update();
        // Tick the dual-wield secondary cooldown alongside the base class's
        // primary one. Cheap to tick unconditionally; only consumed when
        // Zenith dual-wield is active.
        if (secondaryCooldownTimer > 0f) secondaryCooldownTimer -= Time.deltaTime;

        // Stat-gated Zenith auto-activation. Once the player meets ALL four
        // requirements simultaneously, Zenith fires and stays applied for
        // the rest of the run. Cheap check (a few flag reads) — fine to run
        // every frame.
        if (zenithUnlocked && !zenithApplied && Hero.Instance != null)
            CheckZenithAutoActivation(Hero.Instance);
    }

    /// <summary>
    /// Test whether the four Zenith activation requirements are all met
    /// right now and trigger ApplyZenith if so. Idempotent past the first
    /// successful trigger (zenithApplied gates re-entry). Public so other
    /// systems / debug tools can query the same condition.
    /// </summary>
    public bool MeetsZenithRequirements(Hero owner)
    {
        if (owner == null) return false;
        // 1. Both slots are this same SwordWeapon (i.e. dual-wielded).
        bool bothSlots = owner.primaryWeapon == this && owner.secondaryWeapon == this;
        // 2. Slash arc is maxed at 360°.
        bool arcMax = IsSlashArcAtMax;
        // 3. Strictly more than zenithExtraAttacksRequired extra slashes.
        bool extras = extraAttackCount > zenithExtraAttacksRequired;
        // 4. Cooldown reduced by more than zenithAttackSpeedReductionRequired
        //    of the original. OriginalCooldown is captured by the base class
        //    in Start(); guard against a zero from an unspawned-yet weapon.
        bool speed = OriginalCooldown > 0.0001f
            && (1f - cooldown / OriginalCooldown) > zenithAttackSpeedReductionRequired;
        // 5. Player has defeated at least one boss AFTER the curse started.
        bool bossSlain = zenithBossDefeatedSinceCurse;
        return bothSlots && arcMax && extras && speed && bossSlain;
    }

    private void CheckZenithAutoActivation(Hero owner)
    {
        if (zenithApplied) return;
        if (!MeetsZenithRequirements(owner)) return;
        ApplyZenith(owner);
    }

    /// <summary>
    /// Apply Zenith: set the applied flag and double base damage. Does NOT
    /// auto-equip the sword in both slots — the player must pick up a
    /// SECOND sword pickup separately to complete the dual-wield setup.
    /// PowerUpChoiceUI's normal "duplicate weapon" guard is relaxed when
    /// zenithApplied is true so the second sword can actually be equipped.
    /// Idempotent — calling twice is a no-op.
    /// </summary>
    public void ApplyZenith(Hero owner)
    {
        if (zenithApplied) return;
        zenithApplied = true;
        damage *= Mathf.Max(0.01f, zenithDamageMultiplier);
        // Curse lifts now — refund every powerup that was applied at
        // half-efficiency during the trial so the player retroactively
        // gets the full-strength versions.
        if (owner != null) owner.RefundZenithCurseGains();
        Debug.Log($"[Zenith] Applied. Sword damage doubled to {damage}. The trial is over.");
    }

    /// <summary>
    /// Fire from the SECONDARY slot when this sword is dual-wielded. Uses
    /// an independent cooldown timer so the player can attack from both
    /// slots out of sync. Returns true if a swing actually went off (so
    /// Hero can play the attack animation).
    ///
    /// Mirrors the primary slot's chain behavior: extra-attack slashes
    /// fire here too, so the second sword has the SAME stats / output as
    /// the first (without this both slots share the component but the
    /// second slot would only fire 1 slash per cooldown while the first
    /// fires 1 + extraAttackCount).
    /// </summary>
    public bool TryFireSecondary(Hero owner)
    {
        if (!CanFireSecondary || owner == null) return false;
        Fire(owner);
        secondaryCooldownTimer = cooldown;
        if (extraAttackCount > 0)
            StartCoroutine(FireExtras(owner));
        return true;
    }

    protected override void Fire(Hero owner)
    {
        if (slashPrefab == null)
        {
            Debug.LogWarning("SwordWeapon: Slash Prefab is not assigned.");
            return;
        }

        Vector3 spawnPos = owner.transform.position
                         + owner.transform.forward * spawnDistance
                         + Vector3.up * spawnHeight;

        // Parent to the owner so the slash follows the hero's facing while it sweeps.
        SwordSlash slash = Instantiate(slashPrefab, spawnPos, owner.transform.rotation, owner.transform);

        // Boost-driven arc widening — both the visual sweep and the static-fan
        // hitbox grow so a 360° visual actually hits all the way around.
        if (arcDegreesBonus > 0f)
        {
            slash.arcDegrees    += arcDegreesBonus;
            slash.hitArcDegrees += arcDegreesBonus;
        }

        // Boost-driven blade-size scaling — multiplies the slash's reach and
        // visual scale together so the sword feels physically bigger.
        if (bladeSizeMultiplier > 1.0001f)
        {
            slash.transform.localScale *= bladeSizeMultiplier;
            slash.radius           *= bladeSizeMultiplier;
            slash.hitInnerRadius   *= bladeSizeMultiplier;
            slash.hitOuterRadius   *= bladeSizeMultiplier;
            slash.bladeLength      *= bladeSizeMultiplier;
            slash.bladeRadius      *= bladeSizeMultiplier;
        }

        // Zenith mode: switch the slash to the elliptical 360° sweep with a
        // random rainbow trail color. Damage doubling already lives in `damage`
        // (applied at Zenith activation), so no extra multiplier needed here.
        if (zenithApplied)
        {
            slash.zenithMode = true;
            // Override the BladeTrail's authored duration + corner for
            // Zenith-only — must run BEFORE the trail's Start() (which is
            // on the next frame), so doing it right after Instantiate here
            // is safe. Tiny.Trail.Initialize reads both fields when it
            // builds the trail mesh in Start.
            foreach (var trail in slash.GetComponentsInChildren<Tiny.Trail>(true))
            {
                trail.Duration = zenithTrailDuration;
                trail.Corner   = zenithTrailCorner;
            }
            // Per-swing minor-axis jitter: each swing's lateral reach varies
            // slightly so consecutive swings don't trace the exact same arc.
            // Clamped to a small positive minimum so a worst-case jitter
            // can't collapse the ellipse into a forward-only line.
            float jitter = Random.Range(-zenithSemiMinorJitter, zenithSemiMinorJitter);
            float baseMinor = zenithSemiMinor + jitter;
            // Major axis = cursor distance MINUS the blade-middle offset, so
            // the blade's middle (rather than the slash's pivot) lands on
            // the cursor at the swing apex. Snapshot once — the swing's
            // apex is locked even if the mouse moves mid-swing.
            float cursorDist = slashPrefab.radius; // sentinel: the prefab's authored circular radius
            if (owner.TryGetCursorWorldPosition(out Vector3 cursorPos))
            {
                Vector3 flat = cursorPos - owner.transform.position;
                flat.y = 0f;
                cursorDist = flat.magnitude;
                // Pivot stops short of the cursor by zenithBladeMidOffset so
                // the blade extends OUTWARD from there to put its middle on
                // the cursor. Floor at 0.1 so a cursor closer than the
                // offset doesn't invert into a negative radius.
                slash.zenithMajorRadius = Mathf.Max(0.1f, cursorDist - zenithBladeMidOffset);
            }
            // Arc width also scales with cursor distance — far cursors get
            // dramatically wider lateral sweeps. Strength controls how
            // aggressively this scales (0 = constant width, 1 = linear, 2 =
            // quadratic). Applied AFTER the random jitter so each swing
            // still varies a touch.
            float baseR = slashPrefab.radius;
            float widenScale = baseR > 0.001f
                ? Mathf.Pow(Mathf.Max(1f, cursorDist / baseR), zenithSemiMinorWidenStrength)
                : 1f;
            slash.zenithSemiMinor = Mathf.Max(0.05f, baseMinor * widenScale);
            // (Cursor-distance duration scaling reverted — slash.duration
            // stays at the prefab's authored value, 0.2s. The swing always
            // takes a constant amount of time regardless of how far the
            // cursor is.)
            // Random hue, full sat / value gives the punchy rainbow look —
            // each swing rolls its own hue so consecutive swings cycle. The
            // alpha is pulled from zenithTrailAlpha so the rainbow doesn't
            // override the SwordTrail material's authored translucency
            // (HSVToRGB returns alpha=1 by default, which would blow out
            // the trail to fully opaque).
            Color rainbow = Color.HSVToRGB(Random.value, 0.85f, 1.0f);
            rainbow.a = Mathf.Clamp01(zenithTrailAlpha);
            slash.trailTint = rainbow;
        }

        // Apply hero damage multipliers + roll one crit for the whole swing.
        slash.Init(owner.transform, owner.ComputeAttackDamage(damage), enemyLayers, nextSwingRightToLeft);
        nextSwingRightToLeft = !nextSwingRightToLeft;
    }
}
