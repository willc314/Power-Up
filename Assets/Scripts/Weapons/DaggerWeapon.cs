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

    [Header("Elemental Shiv (level-up augment)")]
    [Tooltip("True after the player accepts the Elemental Shiv augment. Each dagger hit (stab + thrown) spawns 2-3 ghostly elemental copies that strike from random angles, deal 80% of dagger damage, and inflict a 3s slow + damage-down debuff.")]
    public bool elementalShivEnabled = false;
    [Tooltip("Optional replacement DaggerStab prefab swapped in once elementalShivEnabled becomes true. Lets you change the dagger model after pickup. Leave null to keep the original stabPrefab.")]
    public DaggerStab elementalStabPrefab;
    [Tooltip("Optional replacement thrown-dagger Projectile prefab swapped in once elementalShivEnabled becomes true. Leave null to keep the original thrownDaggerPrefab.")]
    public Projectile elementalThrownDaggerPrefab;
    [Tooltip("Visual prefab for each spawned ghost clone. Should be a model only (no scripts) — runtime applies a translucent unlit material override using cloneColor. If null, clones are invisible (damage + debuff still apply).")]
    public GameObject elementalCloneVisualPrefab;
    [Tooltip("Local Euler rotation applied to the spawned clone visual so the blade points along the flight direction. Most dagger models point along their local +Z (forward); some point along +X or +Y after import. Tweak in 90° increments — (90,0,0), (-90,0,0), (0,0,90) etc — until the blade visually leads the strike instead of flying sideways.")]
    public Vector3 cloneVisualRotationOffset = Vector3.zero;
    [Tooltip("Base color applied to the clones. Alpha drives the ghostly translucency — 0 = invisible, 1 = opaque. Hue is overridden per clone when useRainbowCloneColor is on.")]
    public Color cloneColor = new Color(0.55f, 0.85f, 1f, 0.4f);
    [Tooltip("If true, each clone gets a random rainbow hue (full saturation + value, alpha taken from cloneColor) so the fan reads as elemental variety rather than a single color. Off uses cloneColor as-is.")]
    public bool useRainbowCloneColor = true;
    [Tooltip("Minimum number of clones spawned per dagger hit (inclusive).")]
    public int cloneCountMin = 2;
    [Tooltip("Maximum number of clones spawned per dagger hit (inclusive).")]
    public int cloneCountMax = 3;
    [Tooltip("Distance from the target where each clone spawns. The clone passes through the target and exits the same distance on the OPPOSITE side, so total travel = 2 × this value. Bump up if the clones feel too close to the target's body.")]
    public float cloneSpawnDistance = 2.5f;
    [Tooltip("Travel duration for each clone, in seconds. Short on purpose — the clone reads as a quick ghost-strike, not a slow-arcing projectile.")]
    public float cloneTravelDuration = 0.18f;
    [Tooltip("Vertical offset above the target's pivot where the clone passes through (so the strike lines up with body height instead of the feet).")]
    public float cloneVerticalOffset = 1.0f;
    [Tooltip("Multiplier on the parent dagger's per-hit damage applied by each clone strike. Default 0.8 = 80% of dagger damage per clone.")]
    public float cloneDamageMultiplier = 0.8f;
    [Tooltip("Slow + damage-down debuff duration applied to the target by each clone (seconds). Refreshes on re-hit; never stacks.")]
    public float shivDebuffDuration = 3f;
    [Tooltip("Movement speed multiplier applied to the target while the debuff is active. 0.7 = 30% slower.")]
    [Range(0f, 1f)] public float shivSlowFactor = 0.7f;
    [Tooltip("Damage multiplier applied to the target's outgoing damage while the debuff is active. 0.7 = target deals 30% less damage.")]
    [Range(0f, 1f)] public float shivDamageFactor = 0.7f;

    private int stabCount;
    private readonly Collider[] fallbackHitBuffer = new Collider[16];

    // Boost behavior:
    //   * AttackSpeed (Dagger pickup) reduces cooldown via the base Weapon
    //     class. If the cooldown floor is hit, post-max boosts roll into a
    //     flat damage gain that hits BOTH stab and thrown damage.
    //   * Projectiles (Crossbow pickup) chains extra stabs/throws spread
    //     across the cooldown. Once extraAttackCount is capped, post-max
    //     boosts roll into the same flat-mirror damage path.
    //   * Damage / Range (Bow/Sword/Shield/Grenade pickups) — base class
    //     would just buff `damage` (stab only), leaving the thrown dagger
    //     untouched. Override mirrors each gain onto thrownDamage too so
    //     the throw scales alongside the stab.

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
            return "+1 Extra Stab";
        }
        // Everything else (Damage, Range, AttackSpeed cooldown-floor case)
        // either falls back to the base class or shows a flat damage gain
        // matching the +N convention used by other weapons.
        return base.DescribeBoost(kind);
    }

    public override bool TryApplyBoost(BoostKind kind)
    {
        if (kind == BoostKind.Projectiles)
        {
            if (IsBoostMaxed(BoostKind.Projectiles))
            {
                ApplyDaggerDamageBoost(postMaxBoostScale);
                return true;
            }
            extraAttackCount++;
            return true;
        }

        if (kind == BoostKind.AttackSpeed)
        {
            // At cooldown floor, the base class converts the boost to flat
            // damage. Mirror that onto thrownDamage too.
            if (cooldown <= minCooldown + 0.001f)
            {
                ApplyDaggerDamageBoost(postMaxBoostScale);
                return true;
            }
            // Otherwise let the base class run its normal cooldown reduction.
            return base.TryApplyBoost(kind);
        }

        // Damage, Range, or any future BoostKind that the base class would
        // route into a flat `damage` increase. Add the same flat amount to
        // BOTH damage AND thrownDamage so both fire paths grow in lockstep.
        {
            float scale = IsBoostMaxed(kind) ? postMaxBoostScale : 1f;
            ApplyDaggerDamageBoost(scale);
            return true;
        }
    }

    /// <summary>
    /// Apply a flat damage gain to BOTH stab `damage` and thrown `thrownDamage`,
    /// mirroring the base-class flat-additive convention so the Powerup UI's
    /// "+N Damage" label matches what other weapons show. Sticking with the
    /// flat add (instead of a multiplier) keeps the description consistent
    /// with Sword / Bow / Shield etc., which the player has built intuitions
    /// around.
    /// </summary>
    private void ApplyDaggerDamageBoost(float scale)
    {
        float gain = damageIncreasePerLevel * scale;
        damage       += gain;
        thrownDamage += gain;
        damageLevel++;
    }

    public override string GetExtraStatsBlock()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Stab Dmg: {damage:0.#}");
        if (thrownDaggerPrefab != null)
            sb.Append($"\nThrow Dmg: {thrownDamage:0.#}");
        if (extraAttackCount > 0)
            sb.Append($"\nExtra Stabs: {extraAttackCount}");
        return sb.ToString();
    }

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
        // Use the elemental dagger prefab once the augment is active and a
        // replacement prefab has been wired up — otherwise stick with the
        // original stabPrefab. Falling through to the original prefab when
        // the elemental one is unassigned avoids a "no visual at all"
        // failure mode if the augment is taken before the user has wired
        // their model.
        DaggerStab prefab = (elementalShivEnabled && elementalStabPrefab != null) ? elementalStabPrefab : stabPrefab;
        if (prefab != null)
        {
            Vector3 spawn = owner.transform.position
                          + owner.transform.forward * stabStartDistance
                          + Vector3.up * stabSpawnHeight;

            DaggerStab stab = Instantiate(prefab, spawn, owner.transform.rotation, owner.transform);
            // One crit roll per stab so all hits from this thrust crit (or don't) together.
            // WithCrit overload feeds the Meteor general augment.
            float stabDmg = owner.ComputeAttackDamageWithCrit(damage, out bool stabCrit);
            stab.Init(owner.transform, stabDmg, enemyLayers, stabStartDistance);
            owner.TryArmMeteorOnProjectile(stab.gameObject, stabDmg, stabCrit, enemyLayers);
            // Elemental Shiv: attach a ShivArmer so DaggerStab's hit loop
            // can spawn the clone fan against each enemy it lands on.
            if (elementalShivEnabled)
            {
                var armer = stab.gameObject.AddComponent<ShivArmer>();
                armer.Arm(this, stabDmg);
            }
            return;
        }

        Debug.LogWarning("DaggerWeapon: Stab Prefab is not assigned. Using fallback damage hitbox with no visual.");
        FallbackStabDamage(owner);
    }

    private void ThrowDagger(Hero owner)
    {
        Projectile prefab = (elementalShivEnabled && elementalThrownDaggerPrefab != null) ? elementalThrownDaggerPrefab : thrownDaggerPrefab;
        if (prefab == null) return;

        Vector3 spawn = owner.transform.position
                      + owner.transform.forward * 0.5f
                      + Vector3.up * throwSpawnHeight;

        Projectile p = Instantiate(prefab, spawn, Quaternion.identity);
        // One crit roll per throw. WithCrit overload feeds the Meteor general augment.
        float dmg = owner.ComputeAttackDamageWithCrit(thrownDamage, out bool wasCrit);
        p.Launch(owner.transform.forward, dmg, enemyLayers);
        owner.TryArmMeteorOnProjectile(p.gameObject, dmg, wasCrit, enemyLayers);
        if (elementalShivEnabled)
        {
            var armer = p.gameObject.AddComponent<ShivArmer>();
            armer.Arm(this, dmg);
        }
    }

    /// <summary>
    /// Spawn the Elemental Shiv clone fan targeting <paramref name="target"/>.
    /// Called by <see cref="ShivArmer.TriggerOn"/> on every enemy hit by
    /// the parent dagger strike. Each clone deals
    /// <paramref name="parentHitDamage"/> × <see cref="cloneDamageMultiplier"/>
    /// and refreshes the slow + damage debuff on the target.
    /// </summary>
    public void SpawnElementalCloneFanOn(Enemy target, float parentHitDamage)
    {
        if (target == null || target.IsDead) return;
        int min = Mathf.Max(0, cloneCountMin);
        int max = Mathf.Max(min, cloneCountMax);
        int count = Random.Range(min, max + 1);
        if (count <= 0) return;

        float perCloneDamage = parentHitDamage * Mathf.Max(0f, cloneDamageMultiplier);
        for (int i = 0; i < count; i++)
        {
            // Random angle in the horizontal plane — clones approach from
            // any direction, no fixed pattern, so consecutive hits feel
            // organic rather than rotating through a baked angle table.
            float angle = Random.Range(0f, 360f);
            // Resolve per-clone color: rainbow mode rolls a fresh random
            // hue at full saturation + value (the punchy "elemental" look)
            // and re-uses cloneColor's alpha so the user can still tune
            // ghostliness via cloneColor.a. Static mode just uses cloneColor.
            Color resolvedColor = useRainbowCloneColor
                ? Color.HSVToRGB(Random.value, 0.85f, 1f)
                : cloneColor;
            if (useRainbowCloneColor) resolvedColor.a = cloneColor.a;
            ElementalShivClone.Spawn(
                target:             target,
                approachAngleDeg:   angle,
                spawnDistance:      cloneSpawnDistance,
                travelDuration:     cloneTravelDuration,
                damage:             perCloneDamage,
                debuffDuration:     shivDebuffDuration,
                debuffSlowFactor:   shivSlowFactor,
                debuffDamageFactor: shivDamageFactor,
                visualPrefab:       elementalCloneVisualPrefab,
                visualRotationOffset: cloneVisualRotationOffset,
                tintColor:          resolvedColor,
                verticalOffset:     cloneVerticalOffset);
        }
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

        // One crit roll for the whole fallback hit (matches the with-prefab path).
        float finalDamage = owner.ComputeAttackDamage(damage);

        for (int i = 0; i < n; i++)
        {
            Enemy enemy = fallbackHitBuffer[i].GetComponentInParent<Enemy>();

            if (enemy != null)
                enemy.TakeDamage(finalDamage);
        }

        if (debugFallbackHitbox)
            Debug.DrawLine(origin, end, Color.magenta, 0.2f);
    }
}
