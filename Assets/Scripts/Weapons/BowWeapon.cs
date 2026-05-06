using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Charging bow.
///   Hold to charge. The bow visual (a Bow.prefab spawned in front of the
///   hero) glows brighter white and the hero is slowed.
///   Release before overcharge time → fires a piercing arrow whose damage
///   and visual size scale with charge time.
///   Hold past overcharge time → on release, fires a Death Beam: a long
///   bright beam in front of the hero that lasts a brief moment, shakes the
///   camera, and pierces everything.
/// </summary>
public class BowWeapon : Weapon
{
    [Header("Bow Visual")]
    [Tooltip("Prefab spawned in front of the hero while charging (e.g. Bow.prefab). Despawned on release.")]
    public GameObject bowVisualPrefab;
    [Tooltip("How far in front of the hero the bow visual sits.")]
    public float bowSpawnDistance = 1.2f;
    [Tooltip("Vertical offset for the bow visual.")]
    public float bowSpawnHeight = 1.0f;
    [Tooltip("Extra Euler rotation applied to the bow visual so the arrow points along the hero's forward. Try (0,-90,0), (0,90,0), or (0,180,0) until it points the right way.")]
    public Vector3 bowVisualRotationOffset = new Vector3(0f, -90f, 0f);
    // The bow used to drive a per-charge material tint (glowColor /
    // emissionIntensity) and an attached point light (useGlowLight /
    // maxLightIntensity / maxLightRange) to telegraph the charge state. Those
    // fields and the accompanying TintBow / CacheBowMaterials machinery were
    // removed when the hero-aura system took over the feedback role — the
    // Lightning aura at full charge and the Plexus aura at overcharge are
    // visually clearer and don't depend on bow shader/material support.

    [Header("Charge")]
    [Tooltip("Seconds to reach full normal-shot charge. After this, damage and arrow size are at max.")]
    public float fullChargeTime = 1.5f;
    [Tooltip("Hero speed multiplier while charging (0 = stuck, 1 = no slowdown).")]
    [Range(0f, 1f)] public float slowdownWhileCharging = 0.4f;
    [Tooltip("Minimum charge time required to actually fire something. Below this, the release does nothing (prevents accidental dry-fires).")]
    public float minReleaseTime = 0.1f;
    [Tooltip("Fraction of incoming damage absorbed while the hero is charging this bow. 0.30 = the hero takes 70% damage while drawing. Read by Hero.TakeDamage and applied before HP subtraction. The slowdown means the player is stuck in place with limited mobility — this reduction makes that risk worthwhile.")]
    [Range(0f, 1f)] public float chargingDamageReduction = 0.30f;

    [Header("Arrow")]
    [Tooltip("Prefab spawned on release for normal shots. Should have a Projectile component (use Arrow_Piercing.prefab).")]
    public Projectile arrowPrefab;
    [Tooltip("If > 0, overrides the projectile prefab's Speed so bow arrows can fly faster than crossbow arrows. Set to 0 to use the prefab's default.")]
    public float arrowSpeed = 45f;
    [Tooltip("Base lifetime (seconds) of every spawned bow arrow before any Range boost. Overrides the prefab's lifetime. Range boosts (Grenade pickups) add arrowLifetimeBonus on top of this — a maxed Range boost takes a 2s arrow up to 6s.")]
    public float arrowBaseLifetime = 2f;
    [Tooltip("How far in front of the hero the arrow spawns. Keep small (e.g. 0.2) so enemies hugging the hero still get hit — large values can spawn the arrow past the target.")]
    public float arrowSpawnForward = 0.2f;
    [Tooltip("Damage at zero charge.")]
    public float arrowMinDamage = 10f;
    [Tooltip("Damage at full charge (and beyond).")]
    public float arrowMaxDamage = 70f;
    [Tooltip("Visual scale multiplier at zero charge.")]
    public float arrowMinScale = 1f;
    [Tooltip("Visual scale multiplier at full charge.")]
    public float arrowMaxScale = 2.5f;

    [Header("Arrow Damage Falloff")]
    [Tooltip("Multiplier applied to the running damage scale after every successful hit. 0.5 = damage halves with each subsequent hit. 1 = no falloff. Combined with arrowDamageFalloffFloor so chained / loop-back hits never drop below a useful floor.")]
    [Range(0f, 1f)] public float arrowDamageFalloffPerHit = 0.5f;
    [Tooltip("Minimum damage multiplier the arrow can fall to. 0.25 = each hit caps at 25% of the original damage no matter how many enemies the arrow has already chained through. Set to 0 for unbounded decay or 1 to disable falloff entirely.")]
    [Range(0f, 1f)] public float arrowDamageFalloffFloor = 0.25f;

    [Header("Death Beam (Overcharge)")]
    [Tooltip("Holding past this many seconds switches the release to a Death Beam.")]
    public float overchargeTime = 5f;
    [Tooltip("Prefab spawned on overcharged release. Should have a DeathBeam component on a stretched cube/cylinder.")]
    public DeathBeam deathBeamPrefab;

    [Header("Audio / VFX (optional)")]
    [Tooltip("Optional particle prefab spawned on the bow when entering overcharge.")]
    public GameObject overchargeVfxPrefab;

    [Header("Hero Aura VFX (optional)")]
    [Tooltip("Spawned on the HERO (not the bow) the moment the bow reaches full charge. Stays on for the rest of the charge until release/interrupt. Designed for Hovl Studio's 'Lightning aura' or any character-aura-style prefab.")]
    public GameObject fullyChargedHeroAuraPrefab;

    [Tooltip("Spawned on the HERO (not the bow) the moment the bow enters overcharge. Stacks ON TOP of the Lightning aura — the fully-charged aura stays alive too. Designed for Hovl Studio's 'Plexus' prefab or any heavier aura you want layered on top.")]
    public GameObject overchargedHeroAuraPrefab;

    [Tooltip("Vertical offset above the hero's pivot where the aura prefabs spawn. Most character-aura prefabs are authored centered on the body, so 0–1 reads well. Tweak if the aura sits at the feet or above the head.")]
    public float heroAuraYOffset = 0f;

    [Tooltip("Uniform scale applied to spawned hero aura prefabs. Useful when the source pack was authored at a different character size than the hero.")]
    public float heroAuraScale = 1f;

    // ---- Runtime state ----
    private bool charging;
    private float chargeTime;
    private GameObject bowVisualInstance;
    private GameObject overchargeVfxInstance;
    private GameObject fullyChargedHeroAuraInstance;
    private GameObject overchargedHeroAuraInstance;
    private bool overchargeReached;
    private bool fullyChargedAuraReached;
    /// <summary>Damage multiplier accumulated while holding past overchargeTime. Applied to beam damage at fire and reset on EndCharge.</summary>
    private float postOverchargeAccumulated;
    /// <summary>Reference to the most recently fired death beam. While non-null (the GameObject is still alive), OnFireDown refuses to start a new charge — the player must wait for the beam to finish.</summary>
    private DeathBeam activeBeam;

    /// <summary>True while the player is actively drawing the bow. Read by Hero.TakeDamage to apply the charging damage reduction.</summary>
    public bool IsCharging => charging;
    /// <summary>True while a previously-fired death beam is still alive in the world. Read by OnFireDown to lock out new charges.</summary>
    public bool IsBeamActive => activeBeam != null;

    public override bool CanFire => true; // charging weapon — always allowed to start

    /// <summary>
    /// True when the assigned death-beam prefab uses the "1% of max HP per
    /// second" damage model. Powerup descriptions hide the irrelevant
    /// "+Beam DPS" string when this is true so the boost label stays honest.
    /// </summary>
    private bool BeamUsesMaxHpDamage =>
        deathBeamPrefab != null && deathBeamPrefab.maxHpFractionPerSecond > 0f;

    // ---- Boost overrides ----
    [Header("Boost Tuning — Arrow Damage")]
    [Tooltip("How much arrowMaxDamage grows per Damage boost.")]
    public float maxDamageIncreasePerLevel = 10f;
    [Tooltip("How much arrowMinDamage also grows per Damage boost.")]
    public float minDamageIncreasePerLevel = 3f;

    [Header("Boost Tuning — Charge Speed")]
    [Tooltip("Floor for fullChargeTime when applying AttackSpeed boosts.")]
    public float minFullChargeTime = 0.4f;
    [Tooltip("Charge speed gained per AttackSpeed boost (fraction). 0.15 = +15% charge speed per pickup. fullChargeTime shrinks accordingly with diminishing returns as it approaches minFullChargeTime.")]
    [Range(0f, 1f)] public float chargeSpeedIncreasePercent = 0.15f;
    [Tooltip("Floor for overchargeTime (when the death beam triggers).")]
    public float minOverchargeTime = 1.5f;
    [Tooltip("Reduction in overchargeTime per AttackSpeed boost (fraction). 0.15 = -15% time-to-beam per pickup, diminishing as it approaches minOverchargeTime.")]
    [Range(0f, 1f)] public float overchargeSpeedIncreasePercent = 0.15f;

    [Header("Fully-Charged Homing")]
    [Tooltip("Charge fraction (0..1) at which arrows gain homing. 1.0 = only fully-charged shots home, 0.95 = slightly-under-full also homes (helps when frame timing nips a few ms off the perceived full charge).")]
    [Range(0.5f, 1f)] public float homingChargeThreshold = 0.95f;
    [Tooltip("Seconds a fully-charged arrow actively homes for, BEFORE the Range boost extends it. The Range boost adds arrowLifetimeBonus to this number too so the homing window grows alongside the arrow's lifespan. Default 2 matches the default arrow base lifetime so a fresh shot homes for its entire life.")]
    public float fullyChargedHomingDuration = 2f;
    [Tooltip("Base degrees/sec the fully-charged arrow can turn at the moment it last hit something. 360 = ~perfect aim (a full U-turn per second). Effective rate ramps higher while the arrow goes without landing a hit (see fullyChargedHomingTurnSpeedRamp).")]
    public float fullyChargedHomingTurnSpeed = 360f;
    [Tooltip("Degrees/sec ADDED to the arrow's effective turn rate per second it spends without landing a hit. Stops the arrow from spiraling around an enemy it can't quite catch — given a couple seconds the turn rate gets tight enough to break the orbit. Reset to zero on every hit. 360 means after 1 unhit second the arrow can U-turn in 0.5s.")]
    public float fullyChargedHomingTurnSpeedRamp = 360f;
    [Tooltip("Hard cap on the ramped turn rate (deg/sec). 0 or negative = uncapped; 2160 caps at six full rotations per second.")]
    public float fullyChargedHomingTurnSpeedMax = 2160f;
    [Tooltip("Range within which the fully-charged arrow looks for targets. Outside this it flies straight.")]
    public float fullyChargedHomingMaxRange = 40f;
    [Tooltip("Pierce count applied to fully-charged arrows so they keep chaining through enemies indefinitely. -1 = infinite pierce (recommended); a positive number caps the chain length.")]
    public int fullyChargedPierceCount = -1;

    [Header("Boost Tuning — Multi-Arrow (Projectiles boost)")]
    [Tooltip("How many arrows the normal attack fires per release. Crossbow pickups bump this by 1 every other upgrade — half the rate the Crossbow itself gets, since the bow's fully-charged shots already chain via homing.")]
    public int arrowProjectileCount = 1;
    [Tooltip("Cap on arrowProjectileCount.")]
    public int maxArrowProjectileCount = 5;
    [Tooltip("Total spread (degrees) for the fan when arrowProjectileCount > 1.")]
    public float arrowSpreadAngle = 25f;
    [Tooltip("Running count of Projectiles (Crossbow) pickups taken on this Bow. Used to gate +1 Arrow to every OTHER upgrade — odd-numbered pickups (1st, 3rd, 5th, …) grant an extra arrow; even-numbered ones still apply the rest of the boost (damage, beam duration, beam DPS) but skip the projectile bump.")]
    public int crossbowPickupCount = 0;

    [Header("Post-Overcharge Damage Rate")]
    [Tooltip("Once the player has held past overchargeTime, every additional second of holding adds this fraction to the death beam's damage multiplier. 0.20 = +20% beam damage per second held. Stack indefinitely if the player wants to commit to a giant nuke.")]
    public float postOverchargeDamageRate = 0.20f;
    [Tooltip("Cap on the post-overcharge damage multiplier (additive). 0 or negative = uncapped. 5 = up to +500% beam damage.")]
    public float maxPostOverchargeMultiplier = 0f;

    [Header("Boost Tuning — Death Beam")]
    [Tooltip("Total bonus DPS added to the spawned death beam from Damage boosts. Set by TryApplyBoost(Damage).")]
    public float deathBeamDpsBonus = 0f;
    [Tooltip("How much beam DPS grows per Damage boost.")]
    public float deathBeamDpsPerBoost = 25f;
    [Tooltip("Bonus radius added to the spawned death beam. Range boosts no longer touch this field — the bow's Range upgrade now boosts the beam's MaxHP%-per-second damage instead. Left in the inspector so it can still be set manually if desired.")]
    public float deathBeamRadiusBonus = 0f;
    [Tooltip("Legacy: used to be how much beam radius grew per Range boost. The Range boost no longer touches the radius (it now bumps deathBeamMaxHpPercentBonus instead), so this value is unused at runtime.")]
    public float deathBeamRadiusPerBoost = 0f;
    [Tooltip("Legacy cap on deathBeamRadiusBonus. Unused now that the Range boost no longer grows the radius.")]
    public float maxDeathBeamRadiusBonus = 0f;
    [Tooltip("Bonus PERCENT added to the death beam's max-HP-per-second damage. 1 = +1%/s of each enemy's max HP. Stacks ON TOP of the prefab's base maxHpFractionPerSecond. Set by TryApplyBoost(Range).")]
    public float deathBeamMaxHpPercentBonus = 0f;
    [Tooltip("Percent points added to deathBeamMaxHpPercentBonus per Range boost. 0.1 = +0.1% Max HP damage per pickup.")]
    public float deathBeamMaxHpPercentBonusPerBoost = 0.1f;
    [Tooltip("Cap on deathBeamMaxHpPercentBonus. Default 2 = up to +2% Max HP/s on top of the prefab's base 1%, so a fully-upgraded Range bow's beam ticks 3% Max HP/s.")]
    public float maxDeathBeamMaxHpPercentBonus = 2f;
    [Tooltip("Total bonus seconds added to each spawned arrow's lifetime from Range boosts (Grenade pickups). Lets fully-charged homing arrows stay alive long enough to chain through more enemies. Set by TryApplyBoost(Range).")]
    public float arrowLifetimeBonus = 0f;
    [Tooltip("How many extra seconds of arrow lifetime each Range boost adds.")]
    public float arrowLifetimeBonusPerBoost = 0.5f;
    [Tooltip("Cap on arrowLifetimeBonus (seconds). Default 4 = up to four extra seconds of homing on top of the prefab's base lifetime.")]
    public float maxArrowLifetimeBonus = 4f;
    [Tooltip("Total bonus pierce count added to each spawned arrow from Range boosts. Stacks ON TOP of the prefab's pierceCount for non-fully-charged shots. Fully-charged shots already pierce infinitely, so this is ignored on them. Set by TryApplyBoost(Range).")]
    public int arrowPierceBonus = 0;
    [Tooltip("How many extra pierces each Range boost adds.")]
    public int arrowPierceBonusPerBoost = 1;
    [Tooltip("Cap on arrowPierceBonus.")]
    public int maxArrowPierceBonus = 8;
    [Tooltip("Total bonus seconds added to the spawned death beam's duration. Bumped by Projectiles boosts (Crossbow pickup) so the beam stays out longer alongside the extra-arrow effect.")]
    public float deathBeamDurationBonus = 0f;
    [Tooltip("How many extra seconds of beam duration each Projectiles boost adds.")]
    public float deathBeamDurationPerBoost = 0.5f;
    [Tooltip("Cap on deathBeamDurationBonus.")]
    public float maxDeathBeamDurationBonus = 4f;

    public override bool IsBoostMaxed(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                // Maxed only when BOTH charge and overcharge times have hit their floors.
                bool chargeFloor = fullChargeTime  <= minFullChargeTime + 0.001f;
                bool overFloor   = overchargeTime  <= minOverchargeTime + 0.001f;
                return chargeFloor && overFloor;

            case BoostKind.Range:
                // Range maxes only when ALL THREE pieces — beam Max HP%,
                // arrow lifetime, and arrow pierce — have hit their caps.
                return deathBeamMaxHpPercentBonus >= maxDeathBeamMaxHpPercentBonus - 0.001f
                    && arrowLifetimeBonus         >= maxArrowLifetimeBonus         - 0.001f
                    && arrowPierceBonus           >= maxArrowPierceBonus;

            case BoostKind.Damage:
                return IsDamageMaxed;

            case BoostKind.Projectiles:
                // Maxed only when arrow count, damage level, AND beam
                // duration are ALL at their caps. Any one of them having
                // room means the boost still has somewhere meaningful to land.
                return arrowProjectileCount >= maxArrowProjectileCount
                    && IsDamageMaxed
                    && deathBeamDurationBonus >= maxDeathBeamDurationBonus - 0.001f;
        }
        return base.IsBoostMaxed(kind);
    }

    public override string DescribeBoost(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.Damage:
                {
                    float scale = IsBoostMaxed(BoostKind.Damage) ? postMaxBoostScale : 1f;
                    return $"+{maxDamageIncreasePerLevel * scale:0.#} Max Damage  •  +{deathBeamDpsPerBoost * scale:0.#} Beam DPS";
                }

            case BoostKind.Projectiles:
                {
                    // Crossbow pickup on Bow: extra arrow on the normal shot,
                    // longer death beam, plus the same damage/beam-DPS gain
                    // Damage gets. Each piece is dropped from the label once
                    // its own cap is reached so the button stays honest.
                    bool arrowFull = arrowProjectileCount >= maxArrowProjectileCount;
                    bool durFull   = deathBeamDurationBonus >= maxDeathBeamDurationBonus - 0.001f;
                    // The Bow only grants +1 Arrow every OTHER pickup. The
                    // NEXT pickup grants when the running counter is even
                    // (since incrementing it lands on an odd number).
                    bool nextGrantsArrow = !arrowFull && (crossbowPickupCount % 2 == 0);
                    float scale = IsDamageMaxed ? postMaxBoostScale : 1f;
                    string dmgPart = $"+{maxDamageIncreasePerLevel * scale:0.#} Max Damage  •  +{deathBeamDpsPerBoost * scale:0.#} Beam DPS";
                    string parts = dmgPart;
                    if (!durFull)         parts = $"+{deathBeamDurationPerBoost:0.##}s Beam  •  " + parts;
                    if (nextGrantsArrow)  parts = "+1 Arrow  •  " + parts;
                    return parts;
                }

            case BoostKind.AttackSpeed:
                {
                    // When both charge and overcharge are at their floors, fall back
                    // to scaled damage so the boost still does something visible.
                    float scale = IsBoostMaxed(BoostKind.AttackSpeed) ? postMaxBoostScale : 1f;
                    bool chargeFloor = fullChargeTime <= minFullChargeTime + 0.001f;
                    bool overFloor   = overchargeTime <= minOverchargeTime + 0.001f;
                    if (chargeFloor && overFloor)
                        return $"+{damageIncreasePerLevel * scale:0.#} Damage";

                    // Apply diminishing returns so the displayed gain shrinks per pickup.
                    float diminish = AttackSpeedDiminisher;
                    float effChargePct = chargeSpeedIncreasePercent     * diminish * scale;
                    float effOverPct   = overchargeSpeedIncreasePercent * diminish * scale;

                    float curC = Mathf.Max(minFullChargeTime, fullChargeTime);
                    float nxtC = Mathf.Max(minFullChargeTime, fullChargeTime / (1f + effChargePct));
                    float curO = Mathf.Max(minOverchargeTime, overchargeTime);
                    float nxtO = Mathf.Max(minOverchargeTime, overchargeTime / (1f + effOverPct));
                    bool chargeStuck = nxtC >= curC;
                    bool overStuck   = nxtO >= curO;
                    string chargePart = chargeStuck ? "Charge maxed" : $"+{(curC / nxtC - 1f) * 100f:0}% Charge";
                    string overPart   = overStuck   ? "Beam maxed"   : $"-{(curO - nxtO):0.0}s to Beam";
                    return chargePart + "  •  " + overPart;
                }

            case BoostKind.Range:
                {
                    if (IsBoostMaxed(BoostKind.Range))
                        return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";

                    // Show whichever pieces of the Range boost still have
                    // headroom — Max HP%, lifetime, and pierce cap independently.
                    bool maxHpFull    = deathBeamMaxHpPercentBonus >= maxDeathBeamMaxHpPercentBonus - 0.001f;
                    bool lifetimeFull = arrowLifetimeBonus  >= maxArrowLifetimeBonus  - 0.001f;
                    bool pierceFull   = arrowPierceBonus    >= maxArrowPierceBonus;
                    var parts = new System.Collections.Generic.List<string>(3);
                    if (!maxHpFull)    parts.Add($"+{deathBeamMaxHpPercentBonusPerBoost:0.##}% Beam Max HP Dmg");
                    if (!lifetimeFull) parts.Add($"+{arrowLifetimeBonusPerBoost:0.##}s Arrow Lifetime");
                    if (!pierceFull)   parts.Add($"+{arrowPierceBonusPerBoost} Pierce");
                    return string.Join("  •  ", parts);
                }
        }
        return base.DescribeBoost(kind);
    }

    public override string GetExtraStatsBlock()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Arrow Dmg: {arrowMinDamage:0.#}–{arrowMaxDamage:0.#}");
        sb.Append($"\nArrows / Shot: {arrowProjectileCount}");
        sb.Append($"\nFull Charge: {fullChargeTime:0.##}s");
        sb.Append($"\nTime to Beam: {overchargeTime:0.##}s");
        if (deathBeamDpsBonus > 0f)      sb.Append($"\nBeam DPS Bonus: +{deathBeamDpsBonus:0.#}");
        // Beam Max HP/s damage: prefab base (always shown) plus any Range-boost
        // bonus from grenade pickups (only shown if non-zero).
        if (BeamUsesMaxHpDamage)
        {
            float basePct = deathBeamPrefab.maxHpFractionPerSecond * 100f;
            float totalPct = basePct + deathBeamMaxHpPercentBonus;
            if (deathBeamMaxHpPercentBonus > 0f)
                sb.Append($"\nBeam Max HP/s: {totalPct:0.##}%  (base {basePct:0.##}% + {deathBeamMaxHpPercentBonus:0.##}%)");
            else
                sb.Append($"\nBeam Max HP/s: {basePct:0.##}%");
        }
        if (deathBeamRadiusBonus > 0f)   sb.Append($"\nBeam Radius +{deathBeamRadiusBonus:0.##}");
        // Show the total arrow lifetime (base + bonus) since the bow
        // explicitly controls the base value now. Always rendered so the
        // player can see how long their arrows stay alive.
        sb.Append($"\nArrow Lifetime: {arrowBaseLifetime + arrowLifetimeBonus:0.##}s");
        if (arrowPierceBonus > 0)        sb.Append($"\nArrow Pierce +{arrowPierceBonus}");
        if (deathBeamDurationBonus > 0f) sb.Append($"\nBeam Duration +{deathBeamDurationBonus:0.##}s");
        sb.Append($"\nOvercharge Rate: +{postOverchargeDamageRate * 100f:0}%/s");
        return sb.ToString();
    }

    /// <summary>
    /// Apply a single damage upgrade to ALL bow damage paths (arrows + beam).
    /// Centralized so post-max fallbacks (capped Range, AttackSpeed at floor)
    /// route to the same fields as the regular Damage boost — Fire() reads
    /// arrowMin/MaxDamage and deathBeamDpsBonus, NOT the base `damage` field,
    /// so any code that just adds to `damage` would be a silent no-op.
    /// </summary>
    private void ApplyBowDamageBoost(float scale)
    {
        damageLevel++;
        damage             += damageIncreasePerLevel    * scale;
        arrowMinDamage     += minDamageIncreasePerLevel * scale;
        arrowMaxDamage     += maxDamageIncreasePerLevel * scale;
        deathBeamDpsBonus  += deathBeamDpsPerBoost      * scale;
    }

    public override bool TryApplyBoost(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.Damage:
                {
                    float scale = IsBoostMaxed(BoostKind.Damage) ? postMaxBoostScale : 1f;
                    ApplyBowDamageBoost(scale);
                    return true;
                }

            case BoostKind.Projectiles:
                {
                    // Crossbow pickup on Bow. Every other upgrade grants
                    // +1 Arrow; the rest of the boost (beam duration,
                    // damage, beam DPS) applies on every pickup.
                    crossbowPickupCount++;
                    bool grantArrow = (crossbowPickupCount % 2 == 1)
                                       && arrowProjectileCount < maxArrowProjectileCount;
                    if (grantArrow) arrowProjectileCount++;

                    deathBeamDurationBonus = Mathf.Min(maxDeathBeamDurationBonus,
                        deathBeamDurationBonus + deathBeamDurationPerBoost);

                    float scale = IsDamageMaxed ? postMaxBoostScale : 1f;
                    ApplyBowDamageBoost(scale);
                    return true;
                }

            case BoostKind.AttackSpeed:
                {
                    float scale = IsBoostMaxed(BoostKind.AttackSpeed) ? postMaxBoostScale : 1f;
                    bool chargeFloor = fullChargeTime <= minFullChargeTime + 0.001f;
                    bool overFloor   = overchargeTime <= minOverchargeTime + 0.001f;
                    if (chargeFloor && overFloor)
                    {
                        // Both floors hit — convert to scaled damage. Route to
                        // the SAME fields the regular Damage boost touches
                        // (arrow min/max, beam DPS bonus); the base `damage`
                        // field is unused by Fire(), so adding to it would be
                        // a silent no-op.
                        ApplyBowDamageBoost(scale);
                        return true;
                    }

                    // Apply diminishing returns: each subsequent AttackSpeed
                    // boost is weaker than the last (shared counter with the
                    // base class so weapon swaps don't reset the curve).
                    float diminish = AttackSpeedDiminisher;
                    float effChargePct = chargeSpeedIncreasePercent     * diminish * scale;
                    float effOverPct   = overchargeSpeedIncreasePercent * diminish * scale;

                    fullChargeTime = Mathf.Max(minFullChargeTime, fullChargeTime / (1f + effChargePct));
                    overchargeTime = Mathf.Max(minOverchargeTime, overchargeTime / (1f + effOverPct));
                    // Faster charging also stacks the post-overcharge damage
                    // rate, so upgrades let the player accumulate beam damage
                    // faster while held past the death-beam threshold (also
                    // diminished so it doesn't run away).
                    postOverchargeDamageRate *= (1f + effChargePct);

                    attackSpeedBoostsTaken++;
                    return true;
                }

            case BoostKind.Range:
                if (IsBoostMaxed(BoostKind.Range))
                {
                    // Both pieces capped — fall back to scaled damage on the
                    // SAME fields the regular Damage boost touches (arrow
                    // min/max, beam DPS bonus). Adding to `damage` would have
                    // no effect since Fire() reads from arrow/beam fields.
                    ApplyBowDamageBoost(postMaxBoostScale);
                    return true;
                }
                // Bump every Range piece toward its cap. Mathf.Min keeps each
                // one from overshooting — capped pieces stop changing while
                // the others can keep growing.
                deathBeamMaxHpPercentBonus = Mathf.Min(maxDeathBeamMaxHpPercentBonus,
                    deathBeamMaxHpPercentBonus + deathBeamMaxHpPercentBonusPerBoost);
                arrowLifetimeBonus = Mathf.Min(maxArrowLifetimeBonus,
                    arrowLifetimeBonus + arrowLifetimeBonusPerBoost);
                arrowPierceBonus = Mathf.Min(maxArrowPierceBonus,
                    arrowPierceBonus + arrowPierceBonusPerBoost);
                return true;
        }
        return base.TryApplyBoost(kind);
    }

    public override bool OnFireDown(Hero owner)
    {
        // Block new charges while the previously-fired death beam is still
        // alive — the player has to wait for the beam to finish before the
        // next shot can begin. activeBeam compares to "fake null" once the
        // beam GameObject is destroyed, so this clears itself automatically.
        if (activeBeam != null) return false;
        StartCharge(owner);
        return false;
    }

    public override bool OnFireHeld(Hero owner)
    {
        if (!charging) return false;
        chargeTime += Time.deltaTime;
        owner.speedMultiplier = slowdownWhileCharging;

        // Update bow visual position (follow the hero).
        if (bowVisualInstance != null)
        {
            bowVisualInstance.transform.position = owner.transform.position
                                                 + owner.transform.forward * bowSpawnDistance
                                                 + Vector3.up * bowSpawnHeight;
            bowVisualInstance.transform.rotation = owner.transform.rotation * Quaternion.Euler(bowVisualRotationOffset);
        }

        float chargeT = Mathf.Clamp01(chargeTime / fullChargeTime);

        // Hero aura: Lightning when fully charged. Spawn ONCE at the
        // chargeT=1 transition; reset flag is in EndCharge so each charge
        // cycle gets a fresh trigger.
        if (chargeT >= 1f && !fullyChargedAuraReached)
        {
            fullyChargedAuraReached = true;
            if (fullyChargedHeroAuraPrefab != null)
                fullyChargedHeroAuraInstance = SpawnHeroAura(owner, fullyChargedHeroAuraPrefab);
        }

        if (chargeTime >= overchargeTime && !overchargeReached)
        {
            overchargeReached = true;
            if (overchargeVfxPrefab != null && bowVisualInstance != null)
                overchargeVfxInstance = Instantiate(overchargeVfxPrefab, bowVisualInstance.transform.position, bowVisualInstance.transform.rotation, bowVisualInstance.transform);

            // Hero aura: Plexus stacked ON TOP of the still-active Lightning
            // aura. Two distinct instances; both get torn down in EndCharge.
            if (overchargedHeroAuraPrefab != null)
                overchargedHeroAuraInstance = SpawnHeroAura(owner, overchargedHeroAuraPrefab);
        }

        // Past overcharge → accumulate beam damage multiplier indefinitely.
        // Each second adds postOverchargeDamageRate to the multiplier; the
        // beam's damagePerSecond is multiplied by (1 + accumulated) on fire.
        if (overchargeReached && postOverchargeDamageRate > 0f)
        {
            postOverchargeAccumulated += postOverchargeDamageRate * Time.deltaTime;
            if (maxPostOverchargeMultiplier > 0f)
                postOverchargeAccumulated = Mathf.Min(maxPostOverchargeMultiplier, postOverchargeAccumulated);
        }

        return false;
    }

    public override void OnInterrupted(Hero owner)
    {
        if (charging) EndCharge(owner);
    }

    public override bool OnFireUp(Hero owner)
    {
        if (!charging) return false;
        bool fired = false;

        if (chargeTime >= minReleaseTime)
        {
            if (chargeTime >= overchargeTime && deathBeamPrefab != null)
            {
                FireDeathBeam(owner);
            }
            else if (arrowPrefab != null)
            {
                FireArrow(owner);
            }
            fired = true;
        }

        EndCharge(owner);
        return fired;
    }

    // ---- Helpers ----

    /// <summary>
    /// Instantiate a hero-aura prefab centered on the hero with the configured
    /// y-offset and uniform scale. Parented to the hero so it follows movement
    /// for the rest of the charge window. Colliders on the prefab are disabled
    /// so the aura can't shove the hero or get caught by the dash phase-through.
    /// </summary>
    private GameObject SpawnHeroAura(Hero owner, GameObject prefab)
    {
        if (prefab == null || owner == null) return null;
        Vector3 pos = owner.transform.position + Vector3.up * heroAuraYOffset;
        GameObject go = Instantiate(prefab, pos, owner.transform.rotation, owner.transform);
        go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, heroAuraScale);
        // Disable any colliders so the aura is purely cosmetic.
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        return go;
    }

    private void StartCharge(Hero owner)
    {
        charging = true;
        chargeTime = 0f;
        overchargeReached = false;
        fullyChargedAuraReached = false;
        postOverchargeAccumulated = 0f; // fresh charge → no banked extra-damage
        owner.speedMultiplier = slowdownWhileCharging;

        if (bowVisualPrefab != null)
        {
            Vector3 pos = owner.transform.position + owner.transform.forward * bowSpawnDistance + Vector3.up * bowSpawnHeight;
            bowVisualInstance = Instantiate(bowVisualPrefab, pos, owner.transform.rotation * Quaternion.Euler(bowVisualRotationOffset));
            // Disable any colliders on the bow visual so it can't shove the hero.
            foreach (var c in bowVisualInstance.GetComponentsInChildren<Collider>()) c.enabled = false;
        }
    }

    private void EndCharge(Hero owner)
    {
        charging = false;
        chargeTime = 0f;
        overchargeReached = false;
        fullyChargedAuraReached = false;
        postOverchargeAccumulated = 0f; // banked damage is consumed/discarded by FireDeathBeam or the interrupt
        owner.speedMultiplier = 1f;
        if (bowVisualInstance != null) Destroy(bowVisualInstance);
        if (overchargeVfxInstance != null) Destroy(overchargeVfxInstance);
        if (fullyChargedHeroAuraInstance != null) Destroy(fullyChargedHeroAuraInstance);
        if (overchargedHeroAuraInstance != null) Destroy(overchargedHeroAuraInstance);
        fullyChargedHeroAuraInstance = null;
        overchargedHeroAuraInstance = null;
    }

    private void FireArrow(Hero owner)
    {
        float chargeT = Mathf.Clamp01(chargeTime / fullChargeTime);
        float dmg = Mathf.Lerp(arrowMinDamage, arrowMaxDamage, chargeT);
        float scale = Mathf.Lerp(arrowMinScale, arrowMaxScale, chargeT);

        // Apply hero damage multipliers + crit roll for this shot. One roll
        // for the whole volley so all arrows in the fan share it.
        dmg = owner.ComputeAttackDamage(dmg);

        // Fully-charged shots gain homing — they curve onto the nearest
        // enemy with near-perfect tracking, then chain to a new target if
        // their current one dies. Piercing is preserved by the prefab.
        bool fullyCharged = chargeT >= homingChargeThreshold;

        Vector3 spawn = owner.transform.position + owner.transform.forward * arrowSpawnForward + Vector3.up * bowSpawnHeight;

        // Total lifetime for this shot — bow controls the base explicitly
        // (overrides the prefab's value) and stacks the Range-boost bonus.
        float effLifetime = Mathf.Max(0.01f, arrowBaseLifetime + Mathf.Max(0f, arrowLifetimeBonus));

        // Damage falloff lives on the Projectile, but the bow controls the
        // policy — so push the inspector values down onto every spawned arrow.
        float falloffPerHit = Mathf.Clamp01(arrowDamageFalloffPerHit);
        float falloffFloor  = Mathf.Clamp01(arrowDamageFalloffFloor);

        int n = Mathf.Max(1, arrowProjectileCount);
        if (n == 1)
        {
            Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
            p.transform.localScale = arrowPrefab.transform.localScale * scale;
            if (arrowSpeed > 0f) p.speed = arrowSpeed;
            ApplyHomingIfCharged(p, fullyCharged);
            // Lifetime + pierce bonuses must be set BEFORE Launch — Launch
            // snapshots pierceCount and schedules Destroy(gameObject, lifetime).
            // Pierce bonus is only applied to non-infinite arrows so the
            // fully-charged shot's infinite-pierce override is preserved.
            p.lifetime = effLifetime;
            if (arrowPierceBonus > 0 && p.pierceCount >= 0) p.pierceCount += arrowPierceBonus;
            p.damageFalloffPerHit = falloffPerHit;
            p.damageFalloffFloor  = falloffFloor;
            p.Launch(owner.transform.forward, dmg, enemyLayers);
            return;
        }

        // Fan the arrows evenly across [-spread/2, +spread/2] around forward.
        // Same shape as the Crossbow's multi-shot, so the upgrade reads the
        // same way visually whether you're holding a Bow or a Crossbow.
        float half = arrowSpreadAngle * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            float angle = Mathf.Lerp(-half, half, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * owner.transform.forward;
            Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
            p.transform.localScale = arrowPrefab.transform.localScale * scale;
            if (arrowSpeed > 0f) p.speed = arrowSpeed;
            ApplyHomingIfCharged(p, fullyCharged);
            p.lifetime = effLifetime;
            if (arrowPierceBonus > 0 && p.pierceCount >= 0) p.pierceCount += arrowPierceBonus;
            p.damageFalloffPerHit = falloffPerHit;
            p.damageFalloffFloor  = falloffFloor;
            p.Launch(dir, dmg, enemyLayers);
        }
    }

    /// <summary>
    /// Toggle homing on a freshly-spawned arrow if the shot was at or above
    /// homingChargeThreshold. Uses near-perfect turn rate so the arrow can
    /// tightly track a moving target, AND overrides pierceCount so the arrow
    /// keeps chaining through enemies for the whole homing window. Must run
    /// BEFORE Launch — Launch snapshots pierceCount into pierceRemaining.
    /// </summary>
    private void ApplyHomingIfCharged(Projectile p, bool fullyCharged)
    {
        if (p == null || !fullyCharged) return;
        p.homing = true;
        p.homingTurnSpeed              = fullyChargedHomingTurnSpeed;
        p.homingTurnSpeedRampPerSecond = fullyChargedHomingTurnSpeedRamp;
        p.homingTurnSpeedMax           = fullyChargedHomingTurnSpeedMax;
        // Range boost extends the homing window in lockstep with the arrow's
        // lifetime, so the arrow can keep homing for as long as it's alive.
        p.homingDuration  = fullyChargedHomingDuration + Mathf.Max(0f, arrowLifetimeBonus);
        p.homingMaxRange  = fullyChargedHomingMaxRange;
        p.homingRetargetEachFrame = true;
        // Override the prefab's pierce count so the homing arrow doesn't
        // burn out after a fixed number of hits (Arrow_Piercing.prefab ships
        // with pierceCount = 5, which would cap the chain).
        p.pierceCount = fullyChargedPierceCount;
    }

    private void FireDeathBeam(Hero owner)
    {
        Vector3 spawn = owner.transform.position;
        DeathBeam beam = Instantiate(deathBeamPrefab, spawn, owner.transform.rotation);
        // Apply boost-driven bonuses to this instance before it initializes
        // (so its damage tick, visual stretch, and lifetime reflect upgrades).
        if (deathBeamRadiusBonus   > 0f) beam.radius   += deathBeamRadiusBonus;
        if (deathBeamDurationBonus > 0f) beam.duration += deathBeamDurationBonus;
        // Range boost: bonus Max HP%-per-second damage. The field on the beam
        // is in fraction units (0.01 = 1%), but the bow tracks the bonus in
        // percent points (0.5 = 0.5%) for readable inspector values.
        if (deathBeamMaxHpPercentBonus > 0f)
            beam.maxHpFractionPerSecond += deathBeamMaxHpPercentBonus / 100f;

        // Bake the post-overcharge damage multiplier into the beam: every
        // second held past overchargeTime added postOverchargeDamageRate to
        // the multiplier, so dps *= (1 + accumulated) before hero modifiers.
        float dps = beam.damagePerSecond + deathBeamDpsBonus;
        if (postOverchargeAccumulated > 0f) dps *= (1f + postOverchargeAccumulated);

        // Roll hero damage modifiers once per beam so the whole beam tick rate
        // is consistently boosted (or critting) for its full duration.
        beam.damagePerSecond = owner.ComputeAttackDamage(dps);
        beam.Init(owner, enemyLayers);

        // Track the active beam so OnFireDown can lock out new charges until
        // it finishes. Unity's null-equivalence on destroyed GameObjects
        // handles the auto-clear; the beam destroys itself when its duration
        // elapses (DeathBeam.Update -> Destroy(gameObject)).
        activeBeam = beam;
    }
}
