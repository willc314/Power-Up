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

    [Header("Damage Scaling")]
    [Tooltip("Multiplicative damage bonus applied to BOTH stab and thrown damage at fire time. Starts at 1, grows with every weapon damage upgrade. Routed through here (instead of bumping the flat `damage` field) so per-weapon damage upgrades buff the stab AND the thrown dagger together — they're two damage paths on the same weapon.")]
    public float daggerDamageMultiplier = 1f;

    [Tooltip("How much daggerDamageMultiplier grows per damage-style boost. 0.15 = +15% per pickup. Replaces the base class's flat damageIncreasePerLevel for the dagger so the throw scales alongside the stab.")]
    public float daggerDamageMultiplierPerBoost = 0.15f;

    private int stabCount;
    private readonly Collider[] fallbackHitBuffer = new Collider[16];

    // Boost behavior:
    //   * AttackSpeed (Dagger pickup) reduces cooldown via the base Weapon
    //     class. If the cooldown floor is hit, post-max boosts roll into
    //     daggerDamageMultiplier (buffs stab + throw together).
    //   * Projectiles (Crossbow pickup) chains extra stabs/throws spread
    //     across the cooldown. Because Fire() increments stabCount on every
    //     call, those follow-ups participate in the every-Nth-throw logic too.
    //     Once extraAttackCount is capped, post-max boosts roll into the
    //     damage multiplier.
    //   * Damage / Range (Bow/Sword/Shield/Grenade pickups) — the base class
    //     would just add flat `damage` (which only buffs the stab). Override
    //     to route into daggerDamageMultiplier so the throw scales too.

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
                return $"+{daggerDamageMultiplierPerBoost * postMaxBoostScale * 100f:0}% Damage";
            return "+1 Extra Stab";
        }
        if (kind == BoostKind.AttackSpeed)
        {
            // Default base-class description handles this — but if the
            // cooldown floor is hit, the boost is converted to the dagger's
            // multiplier instead of flat damage. Surface that in the label so
            // the player sees the actual delta.
            if (cooldown <= minCooldown + 0.001f)
                return $"+{daggerDamageMultiplierPerBoost * postMaxBoostScale * 100f:0}% Damage";
            return base.DescribeBoost(kind);
        }
        // Damage / Range / anything else falls back to the multiplier-based
        // description since TryApplyBoost converts those to multiplier gains.
        return $"+{daggerDamageMultiplierPerBoost * (IsBoostMaxed(kind) ? postMaxBoostScale : 1f) * 100f:0}% Damage";
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
            // At cooldown floor, the base class would add flat damage —
            // intercept and route to the multiplier so the throw scales too.
            if (cooldown <= minCooldown + 0.001f)
            {
                ApplyDaggerDamageBoost(postMaxBoostScale);
                return true;
            }
            // Otherwise let the base class run its normal cooldown reduction.
            return base.TryApplyBoost(kind);
        }

        // Damage, Range, or any future BoostKind — base class would only buff
        // `damage` (stab), leaving the thrown dagger untouched. Convert into a
        // multiplier that scales both fire paths instead.
        {
            float scale = IsBoostMaxed(kind) ? postMaxBoostScale : 1f;
            ApplyDaggerDamageBoost(scale);
            return true;
        }
    }

    /// <summary>
    /// Increment the dagger's damage multiplier by the per-boost amount × scale.
    /// Centralized so every "would-add-flat-damage" path (post-max Projectiles,
    /// AttackSpeed at floor, Damage, Range) produces a consistent multiplier
    /// gain that buffs BOTH stab and thrown damage equally at fire time.
    /// </summary>
    private void ApplyDaggerDamageBoost(float scale)
    {
        daggerDamageMultiplier += daggerDamageMultiplierPerBoost * scale;
        damageLevel++;
    }

    public override string GetExtraStatsBlock()
    {
        var sb = new System.Text.StringBuilder();
        // Show effective damage including the multiplier so the player can
        // see what stab and throw will actually deal before hero modifiers.
        sb.Append($"Stab Dmg: {damage * daggerDamageMultiplier:0.#}");
        if (thrownDaggerPrefab != null)
            sb.Append($"\nThrow Dmg: {thrownDamage * daggerDamageMultiplier:0.#}");
        if (daggerDamageMultiplier > 1.001f)
            sb.Append($"\nDmg Mult: ×{daggerDamageMultiplier:0.00}");
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
        if (stabPrefab != null)
        {
            Vector3 spawn = owner.transform.position
                          + owner.transform.forward * stabStartDistance
                          + Vector3.up * stabSpawnHeight;

            DaggerStab stab = Instantiate(stabPrefab, spawn, owner.transform.rotation, owner.transform);
            // One crit roll per stab so all hits from this thrust crit (or don't) together.
            // daggerDamageMultiplier baked in so post-max boosts buff stab + throw together.
            stab.Init(owner.transform, owner.ComputeAttackDamage(damage * daggerDamageMultiplier), enemyLayers, stabStartDistance);
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
        // One crit roll per throw. daggerDamageMultiplier baked in so the
        // throw scales with weapon upgrades alongside the stab.
        p.Launch(owner.transform.forward, owner.ComputeAttackDamage(thrownDamage * daggerDamageMultiplier), enemyLayers);
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
        // daggerDamageMultiplier baked in for parity with the prefab path.
        float finalDamage = owner.ComputeAttackDamage(damage * daggerDamageMultiplier);

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