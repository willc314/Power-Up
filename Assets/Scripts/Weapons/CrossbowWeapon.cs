using UnityEngine;

/// <summary>
/// Fires straight arrows at moderate speed on every press / cooldown tick.
/// While the fire button is held, a crossbow visual sits in front of the hero.
/// </summary>
public class CrossbowWeapon : Weapon
{
    [Header("Crossbow")]
    [Tooltip("Prefab spawned per shot. Should have a Projectile component on its root, ideally wrapping Arrow_Regular.")]
    public Projectile arrowPrefab;
    [Tooltip("How far in front of the hero the arrow spawns.")]
    public float spawnDistance = 0.8f;
    [Tooltip("Vertical offset above the hero's pivot.")]
    public float spawnHeight = 1.0f;

    [Header("Power Boost (Multi-Shot)")]
    [Tooltip("How many arrows fire per pull of the trigger. Crossbow's intrinsic upgrade adds 1 per level.")]
    public int projectileCount = 1;
    [Tooltip("Maximum number of arrows the crossbow can fire at once.")]
    public int maxProjectileCount = 7;
    [Tooltip("Total spread (degrees) the fan of arrows covers when projectileCount > 1.")]
    public float spreadAngle = 30f;

    [Header("Crossbow Visual")]
    [Tooltip("Prefab shown in the hero's hands while holding the fire button (e.g. Crossbow.prefab). Despawned on release.")]
    public GameObject crossbowVisualPrefab;
    [Tooltip("How far in front of the hero the visual sits.")]
    public float visualSpawnDistance = 1.0f;
    [Tooltip("Vertical offset for the visual.")]
    public float visualSpawnHeight = 1.0f;
    [Tooltip("Extra Euler rotation so the crossbow points along the hero's forward. Try (0,-90,0), (0,90,0), or (0,180,0) until it points right.")]
    public Vector3 visualRotationOffset = new Vector3(0f, -90f, 0f);

    private GameObject visualInstance;

    public override bool OnFireDown(Hero owner)
    {
        SpawnVisual(owner);
        return false;
    }

    public override bool OnFireHeld(Hero owner)
    {
        UpdateVisualPose(owner);
        return TryFire(owner); // fires arrows on cooldown while held
    }

    public override bool OnFireUp(Hero owner)
    {
        DespawnVisual();
        return false;
    }

    public override void OnInterrupted(Hero owner)
    {
        // Hero calls this when something cancels the player's input mid-hold
        // (e.g. dashing, opening the powerup choice panel). Without this, the
        // crossbow visual would stay parented in front of the hero.
        DespawnVisual();
    }

    protected override void Fire(Hero owner)
    {
        if (arrowPrefab == null) { Debug.LogWarning("CrossbowWeapon: Arrow Prefab not assigned."); return; }
        Vector3 spawn = owner.transform.position
                      + owner.transform.forward * spawnDistance
                      + Vector3.up * spawnHeight;

        // One crit roll for the entire volley so all arrows in the fan share it.
        // Use the crit-aware overload so the Meteor general augment can gate
        // its per-fire chance roll on crit (no duplicate roll).
        float finalDamage = owner.ComputeAttackDamageWithCrit(damage, out bool wasCrit);

        int n = Mathf.Max(1, projectileCount);
        if (n == 1)
        {
            Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
            p.Launch(owner.transform.forward, finalDamage, enemyLayers);
            owner.TryArmMeteorOnProjectile(p.gameObject, finalDamage, wasCrit, enemyLayers);
            return;
        }

        // Fan the arrows evenly across [-spread/2, +spread/2] around forward.
        // For odd counts the middle arrow goes straight; for even counts the
        // pair straddles the forward direction. Meteor arms the FIRST arrow
        // in the fan only — semantics are "one meteor per fire-event" so
        // multi-shot doesn't multiply meteors.
        float half = spreadAngle * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float t = (n == 1) ? 0.5f : (float)i / (n - 1);
            float angle = Mathf.Lerp(-half, half, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * owner.transform.forward;
            Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
            p.Launch(dir, finalDamage, enemyLayers);
            if (i == 0) owner.TryArmMeteorOnProjectile(p.gameObject, finalDamage, wasCrit, enemyLayers);
        }
    }

    // ---- Boost overrides ----

    public override bool IsBoostMaxed(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles) return projectileCount >= maxProjectileCount;
        return base.IsBoostMaxed(kind);
    }

    public override string DescribeBoost(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles)
        {
            // At hard projectile cap, post-max boosts fall back to scaled damage.
            if (IsBoostMaxed(BoostKind.Projectiles))
                return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
            return "+1 Projectile";
        }
        return base.DescribeBoost(kind);
    }

    public override string GetExtraStatsBlock()
    {
        return $"Projectiles / Shot: {projectileCount}\nSpread: {spreadAngle:0}°";
    }

    public override bool TryApplyBoost(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles)
        {
            if (IsBoostMaxed(BoostKind.Projectiles))
            {
                // At max projectiles — keep boosts useful as scaled damage.
                damage     += damageIncreasePerLevel * postMaxBoostScale;
                damageLevel++;
                return true;
            }
            projectileCount++;
            return true;
        }
        return base.TryApplyBoost(kind);
    }

    private void SpawnVisual(Hero owner)
    {
        if (crossbowVisualPrefab == null) return;
        if (visualInstance != null) Destroy(visualInstance);
        Vector3 pos = owner.transform.position
                    + owner.transform.forward * visualSpawnDistance
                    + Vector3.up * visualSpawnHeight;
        visualInstance = Instantiate(crossbowVisualPrefab, pos,
            owner.transform.rotation * Quaternion.Euler(visualRotationOffset));
        // Make sure no collider on the visual shoves the hero around.
        foreach (var c in visualInstance.GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    private void UpdateVisualPose(Hero owner)
    {
        if (visualInstance == null) return;
        visualInstance.transform.position = owner.transform.position
            + owner.transform.forward * visualSpawnDistance
            + Vector3.up * visualSpawnHeight;
        visualInstance.transform.rotation = owner.transform.rotation * Quaternion.Euler(visualRotationOffset);
    }

    private void DespawnVisual()
    {
        if (visualInstance != null) Destroy(visualInstance);
        visualInstance = null;
    }

    private void OnDisable() { DespawnVisual(); }
}
