using UnityEngine;

/// <summary>
/// Secondary throwable weapon: launches a shield in front of the hero. The
/// shield flies out, returns, and despawns when caught. Multiple shields can
/// be in flight at once — fire rate is gated only by the base cooldown.
/// </summary>
public class ShieldWeapon : Weapon
{
    [Header("Shield")]
    [Tooltip("Prefab spawned when thrown. Should have a ShieldThrow component on its root, ideally wrapping a shield model (e.g. Shield05Polyart).")]
    public ShieldThrow shieldPrefab;
    [Tooltip("How far in front of the hero the shield spawns.")]
    public float spawnDistance = 0.8f;
    [Tooltip("Vertical offset above the hero's pivot.")]
    public float spawnHeight = 1.0f;

    protected override void Fire(Hero owner)
    {
        if (shieldPrefab == null)
        {
            Debug.LogWarning("ShieldWeapon: Shield Prefab is not assigned.");
            return;
        }

        Vector3 spawnPos = owner.transform.position
                         + owner.transform.forward * spawnDistance
                         + Vector3.up * spawnHeight;

        ShieldThrow shield = Instantiate(shieldPrefab, spawnPos, owner.transform.rotation);
        // Apply hero damage multipliers + crit roll (one roll per throw).
        shield.Init(owner.transform, owner.ComputeAttackDamage(damage), enemyLayers, owner.transform.forward);
    }

    // Crossbow pickup → Projectiles boost. On the Shield, this chains an
    // extra throw spread across the cooldown — they boomerang back
    // independently so multiple shields can be in flight at once.
    public override bool IsBoostMaxed(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles) return extraAttackCount >= maxExtraAttackCount;
        return base.IsBoostMaxed(kind);
    }

    public override string DescribeBoost(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles)
        {
            if (IsBoostMaxed(BoostKind.Projectiles))
                return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
            return "+1 Extra Shield";
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
        return base.TryApplyBoost(kind);
    }
}
