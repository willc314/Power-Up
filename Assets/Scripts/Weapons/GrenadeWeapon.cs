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

    [Header("Power Boost (Range)")]
    [Tooltip("Total bonus added to the explosion radius from Range boosts. Set by TryApplyBoost(Range).")]
    public float explosionRadiusBonus = 0f;
    [Tooltip("Radius added to the explosion per power level.")]
    public float radiusIncreasePerLevel = 1.0f;
    [Tooltip("Cap on explosionRadiusBonus.")]
    public float maxExplosionRadiusBonus = 5f;

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
        g.Launch(startPos, endPos, arcHeight, flightTime);
    }

    // ---- Boost overrides ----

    public override bool IsBoostMaxed(BoostKind kind)
    {
        if (kind == BoostKind.Range) return explosionRadiusBonus >= maxExplosionRadiusBonus - 0.001f;
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
        return base.DescribeBoost(kind);
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
        return base.TryApplyBoost(kind);
    }
}
