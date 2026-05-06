using UnityEngine;

/// <summary>
/// Slowly throws an explosive grenade that lands a fixed distance in front of
/// the hero and detonates in a large AOE.
/// </summary>
public class GrenadeWeapon : Weapon
{
    [Header("Grenade")]
    [Tooltip("Prefab spawned per throw. Should have a Grenade component.")]
    public Grenade grenadePrefab;
    [Tooltip("World units in front of the hero where the grenade lands.")]
    public float throwDistance = 6f;
    [Tooltip("Peak height of the throw arc.")]
    public float arcHeight = 3f;
    [Tooltip("Seconds the grenade spends in flight.")]
    public float flightTime = 0.8f;
    [Tooltip("Vertical offset for the throw release point.")]
    public float spawnHeight = 1.0f;

    [Header("Throw SFX")]
    [Tooltip("One-shot sound played at the hero's position when the grenade leaves the hand. Routed through SoundManager.")]
    public AudioClip throwSound;
    [Tooltip("Per-clip volume multiplier for the throw SFX. Stacks on SoundManager.volume.")]
    [Range(0f, 1f)] public float throwSoundVolume = 1f;

    [Header("Power Boost (Range)")]
    [Tooltip("Total bonus added to the explosion radius from Range boosts. Set by TryApplyBoost(Range).")]
    public float explosionRadiusBonus = 0f;
    [Tooltip("Radius added to the explosion per power level.")]
    public float radiusIncreasePerLevel = 1.0f;
    [Tooltip("Cap on explosionRadiusBonus.")]
    public float maxExplosionRadiusBonus = 5f;

    private void Awake()
    {
        weaponName = "Grenade";
        weaponType = eWeaponType.grenade;
        // The actual AOE damage lives on the Explosion prefab — pull it onto
        // the weapon's own `damage` field at startup so per-weapon upgrades
        // (which all add to `damage`) actually feed back into Fire(). Without
        // this Fire() would read directly from the prefab and any post-max
        // damage boosts on the weapon would be silent no-ops.
        if (grenadePrefab != null && grenadePrefab.explosionPrefab != null)
            damage = grenadePrefab.explosionPrefab.damage;
    }

    protected override void Fire(Hero owner)
    {
        if (grenadePrefab == null) { Debug.LogWarning("GrenadeWeapon: Grenade Prefab not assigned."); return; }

        Vector3 startPos = owner.transform.position
                         + owner.transform.forward * 0.5f
                         + Vector3.up * spawnHeight;
        Vector3 endPos = owner.transform.position
                         + owner.transform.forward * throwDistance;

        Grenade g = Instantiate(grenadePrefab, startPos, Quaternion.identity);
        g.radiusBonus = explosionRadiusBonus;
        // Throw SFX at the hero's release point (slightly forward + up,
        // matches the visual spawn position so the audio's spatialization
        // tracks the throw rather than the hero's pivot).
        if (throwSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySfxAt(throwSound, startPos, throwSoundVolume);
        // Bake hero damage modifiers (general damage + crit) into the
        // explosion's damage. Source is the weapon's own `damage` field,
        // which Awake() seeded from the explosion prefab and which weapon
        // upgrades (Range cap, Projectiles cap, Damage fallback) keep
        // adding to via the base TryApplyBoost path. WithCrit overload
        // feeds the Meteor general augment.
        float dmg = owner.ComputeAttackDamageWithCrit(damage, out bool wasCrit);
        g.damageOverride = dmg;
        // Pre-crit friendly-fire damage so the player's crit roll doesn't
        // amplify their own self-damage if they stand in the AOE. The
        // multiplier-of-0.5x is preserved (matches the original Explosion
        // behavior) but applied to the no-crit base instead.
        g.friendlyFireDamage = owner.ComputeAttackDamageNoCrit(damage) * 0.5f;
        g.Launch(startPos, endPos, arcHeight, flightTime);
        // Meteor arms the GRENADE itself — Grenade.Detonate() forwards the
        // armer onto the spawned Explosion so the meteor fires when the
        // explosion lands its first enemy hit.
        owner.TryArmMeteorOnProjectile(g.gameObject, dmg, wasCrit, enemyLayers);
    }

    // ---- Boost overrides ----

    public override bool IsBoostMaxed(BoostKind kind)
    {
        if (kind == BoostKind.Range)       return explosionRadiusBonus >= maxExplosionRadiusBonus - 0.001f;
        if (kind == BoostKind.Projectiles) return extraAttackCount >= maxExtraAttackCount;
        return base.IsBoostMaxed(kind);
    }

    public override string DescribeBoost(BoostKind kind)
    {
        if (kind == BoostKind.Range)
        {
            // Past the radius cap, post-max boosts fall back to scaled damage.
            if (IsBoostMaxed(BoostKind.Range))
                return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
            return $"+{radiusIncreasePerLevel:0.#} Explosion Radius";
        }
        if (kind == BoostKind.Projectiles)
        {
            // Crossbow pickup → chains an extra grenade after a short delay.
            if (IsBoostMaxed(BoostKind.Projectiles))
                return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
            return "+1 Extra Grenade";
        }
        return base.DescribeBoost(kind);
    }

    public override string GetExtraStatsBlock()
    {
        var sb = new System.Text.StringBuilder();
        if (explosionRadiusBonus > 0f) sb.Append($"Explosion Radius +{explosionRadiusBonus:0.##}");
        if (extraAttackCount > 0)      { if (sb.Length > 0) sb.Append('\n'); sb.Append($"Extra Grenades: {extraAttackCount}"); }
        return sb.ToString();
    }

    public override bool TryApplyBoost(BoostKind kind)
    {
        if (kind == BoostKind.Range)
        {
            if (IsBoostMaxed(BoostKind.Range))
            {
                // Radius capped — convert post-max boosts into scaled damage.
                damage     += damageIncreasePerLevel * postMaxBoostScale;
                damageLevel++;
                return true;
            }
            explosionRadiusBonus = Mathf.Min(maxExplosionRadiusBonus,
                explosionRadiusBonus + radiusIncreasePerLevel);
            return true;
        }
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
        return base.TryApplyBoost(kind);
    }
}
