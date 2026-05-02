using UnityEngine;

/// <summary>
/// Quick-stab dagger. Every Throw Every N Stabs (default 4), the stab is
/// replaced by a thrown dagger projectile.
/// </summary>
public class DaggerWeapon : Weapon
{
    [Header("Stab")]
    [Tooltip("Prefab spawned for each stab. Should have a DaggerStab component on its root.")]
    public DaggerStab stabPrefab;
    [Tooltip("How far in front of the hero the stab starts.")]
    public float stabStartDistance = 0.4f;

    [Header("Throw")]
    [Tooltip("Prefab spawned for the thrown dagger. Should have a Projectile component on its root.")]
    public Projectile thrownDaggerPrefab;
    [Tooltip("Damage of the thrown dagger (separate from the stab damage).")]
    public float thrownDamage = 20f;
    [Tooltip("Throw the dagger every Nth attack (e.g. 4 = throw on every 4th attack).")]
    public int throwEveryNStabs = 4;
    [Tooltip("Vertical offset for the thrown dagger spawn.")]
    public float throwSpawnHeight = 1.0f;

    private int stabCount;

    protected override void Fire(Hero owner)
    {
        stabCount++;
        bool throwThisOne = throwEveryNStabs > 0 && (stabCount % throwEveryNStabs == 0);

        if (throwThisOne && thrownDaggerPrefab != null)
        {
            Vector3 spawn = owner.transform.position + owner.transform.forward * 0.5f + Vector3.up * throwSpawnHeight;
            Projectile p = Instantiate(thrownDaggerPrefab, spawn, Quaternion.identity);
            p.Launch(owner.transform.forward, thrownDamage, enemyLayers);
        }
        else if (stabPrefab != null)
        {
            Vector3 spawn = owner.transform.position + owner.transform.forward * stabStartDistance + Vector3.up;
            DaggerStab stab = Instantiate(stabPrefab, spawn, owner.transform.rotation, owner.transform);
            stab.Init(owner.transform, damage, enemyLayers, stabStartDistance);
        }
    }
}
