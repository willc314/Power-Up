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
        // WithCrit overload feeds the Meteor general augment.
        float dmg = owner.ComputeAttackDamageWithCrit(damage, out bool wasCrit);
        shield.Init(owner.transform, dmg, enemyLayers, owner.transform.forward);
        owner.TryArmMeteorOnProjectile(shield.gameObject, dmg, wasCrit, enemyLayers);
    }

    public override string GetExtraStatsBlock()
    {
        // Surface the upgrade-tracked extra-shield count plus a few static
        // throw stats lifted from the prefab. Keeps the in-game stats panel
        // populated so the shield isn't a blank entry like it used to be.
        var sb = new System.Text.StringBuilder();
        if (shieldPrefab != null)
        {
            float throwRange = Mathf.Max(0f, shieldPrefab.throwSpeed) * Mathf.Max(0f, shieldPrefab.outboundDuration);
            sb.Append($"Throw Range: {throwRange:0.#}");
            sb.Append($"\nThrow Speed: {shieldPrefab.throwSpeed:0.#}");
            sb.Append($"\nReturn Speed: {shieldPrefab.returnSpeed:0.#}");
            sb.Append($"\nHit Radius: {shieldPrefab.hitRadius:0.##}");
        }
        sb.Append($"\nExtra Shields: {extraAttackCount}");
        // Trim a leading newline if the prefab block was empty.
        return sb.ToString().TrimStart('\n');
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
