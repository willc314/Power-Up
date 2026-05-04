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

    // Toggles each fire so successive swings come from opposite sides.
    private bool nextSwingRightToLeft = false;

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

        // Apply hero damage multipliers + roll one crit for the whole swing.
        slash.Init(owner.transform, owner.ComputeAttackDamage(damage), enemyLayers, nextSwingRightToLeft);
        nextSwingRightToLeft = !nextSwingRightToLeft;
    }
}
