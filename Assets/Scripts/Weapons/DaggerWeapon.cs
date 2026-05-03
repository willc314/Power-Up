using UnityEngine;

/// <summary>
/// Quick-stab dagger weapon.
/// This weapon is meant to replace the sword as a close-range primary weapon.
///
/// Behavior:
///   * Most attacks spawn a DaggerStab visual/hitbox in front of the Hero.
///   * Every Nth stab can instead throw a dagger projectile.
///   * If the stab prefab is missing, the script still performs a fallback
///     hit check so the weapon does not feel broken while you are setting up prefabs.
///
/// Setup:
///   1. Add DaggerWeapon to the Hero.
///   2. Drag this component into Hero's Dagger Weapon field.
///   3. Assign Stab Prefab to a prefab with DaggerStab.cs.
///   4. Assign Thrown Dagger Prefab if you want the every-Nth attack throw.
///   5. Make sure Enemy Layers includes the Enemy layer.
/// </summary>
public class DaggerWeapon : Weapon
{
    [Header("Stab")]
    [Tooltip("Prefab spawned for each stab. Should have a DaggerStab component on its root.")]
    public DaggerStab stabPrefab;

    [Tooltip("How far in front of the Hero the stab starts.")]
    public float stabStartDistance = 0.45f;

    [Tooltip("Vertical offset above the Hero pivot where the stab appears.")]
    public float stabSpawnHeight = 1.0f;

    [Header("Fallback Stab Hitbox")]
    [Tooltip("Used only if Stab Prefab is missing. Distance the fallback stab reaches in front of the Hero.")]
    public float fallbackStabRange = 1.8f;

    [Tooltip("Used only if Stab Prefab is missing. Radius of the fallback stab hitbox.")]
    public float fallbackStabRadius = 0.45f;

    [Tooltip("Used only if Stab Prefab is missing. Vertical height of the fallback stab hitbox.")]
    public float fallbackStabHeight = 1.5f;

    [Tooltip("Draws the fallback hitbox direction in Scene view.")]
    public bool debugFallbackHitbox = true;

    [Header("Throw")]
    [Tooltip("Prefab spawned for the thrown dagger. Should have a Projectile component on its root.")]
    public Projectile thrownDaggerPrefab;

    [Tooltip("Damage of the thrown dagger. Separate from the stab damage.")]
    public float thrownDamage = 20f;

    [Tooltip("Throw the dagger every Nth attack. Set to 0 to disable throwing.")]
    public int throwEveryNStabs = 0;

    [Tooltip("Vertical offset for the thrown dagger spawn.")]
    public float throwSpawnHeight = 1.0f;

    private int stabCount;
    private readonly Collider[] fallbackHitBuffer = new Collider[16];

    // Boost behavior (AttackSpeed = -cooldown) is fully handled by the base
    // Weapon class via its minCooldown / cooldownReductionPerBoost fields, so
    // the Dagger doesn't need its own overrides.

    private void Reset()
    {
        weaponName = "Dagger";
        weaponType = eWeaponType.dagger;
        cooldown = 0.25f;
        damage = 15f;
    }

    private void Awake()
    {
        weaponName = "Dagger";
        weaponType = eWeaponType.dagger;
    }

    protected override void Fire(Hero owner)
    {
        if (owner == null)
            return;

        stabCount++;

        bool throwThisOne = throwEveryNStabs > 0 && (stabCount % throwEveryNStabs == 0);

        if (throwThisOne && thrownDaggerPrefab != null)
        {
            ThrowDagger(owner);
            return;
        }

        Stab(owner);
    }

    private void Stab(Hero owner)
    {
        if (stabPrefab != null)
        {
            Vector3 spawn = owner.transform.position
                          + owner.transform.forward * stabStartDistance
                          + Vector3.up * stabSpawnHeight;

            DaggerStab stab = Instantiate(stabPrefab, spawn, owner.transform.rotation, owner.transform);
            stab.Init(owner.transform, damage, enemyLayers, stabStartDistance);
            return;
        }

        Debug.LogWarning("DaggerWeapon: Stab Prefab is not assigned. Using fallback damage hitbox with no visual.");
        FallbackStabDamage(owner);
    }

    private void ThrowDagger(Hero owner)
    {
        Vector3 spawn = owner.transform.position
                      + owner.transform.forward * 0.5f
                      + Vector3.up * throwSpawnHeight;

        Projectile p = Instantiate(thrownDaggerPrefab, spawn, Quaternion.identity);
        p.Launch(owner.transform.forward, thrownDamage, enemyLayers);
    }

    private void FallbackStabDamage(Hero owner)
    {
        Vector3 origin = owner.transform.position + Vector3.up * (fallbackStabHeight * 0.5f);
        Vector3 end = origin + owner.transform.forward * fallbackStabRange;

        int n = Physics.OverlapCapsuleNonAlloc(
            origin,
            end,
            fallbackStabRadius,
            fallbackHitBuffer,
            enemyLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < n; i++)
        {
            Enemy enemy = fallbackHitBuffer[i].GetComponentInParent<Enemy>();

            if (enemy != null)
                enemy.TakeDamage(damage);
        }

        if (debugFallbackHitbox)
            Debug.DrawLine(origin, end, Color.magenta, 0.2f);
    }
}