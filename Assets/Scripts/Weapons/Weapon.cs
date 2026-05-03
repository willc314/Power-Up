using UnityEngine;

/// <summary>
/// Weapon type IDs used by powerups and weapon replacement.
/// These names let a dropped PowerUp tell the Hero which weapon it represents.
/// </summary>
public enum eWeaponType
{
    none,
    sword,
    shield,
    bow,
    dagger,
    crossbow,
    grenade
}

/// <summary>
/// Categories of stat boost a powerup can grant. The picked-up weapon
/// determines which kind of boost is offered; each Weapon decides how to
/// apply that kind to itself.
/// </summary>
public enum BoostKind
{
    /// <summary>+damage. Default fallback for any kind a weapon doesn't specially handle.</summary>
    Damage,
    /// <summary>More projectiles per shot. Crossbow specialty; falls back to +damage on others.</summary>
    Projectiles,
    /// <summary>Bigger AOE / longer reach. Grenade specialty; falls back to +damage on others.</summary>
    Range,
    /// <summary>Lower cooldown / faster charge. Dagger specialty; base reduces cooldown for everyone.</summary>
    AttackSpeed,
}

/// <summary>
/// Abstract base class for all weapons.
/// A weapon is a MonoBehaviour you put on the Hero or a child of the Hero.
/// The Hero references one as Primary and one as Secondary.
///
/// Each subclass decides what happens on Fire(): spawn a slash visual, throw a
/// projectile, summon an aura, etc. The base class handles cooldown.
///
/// This version supports:
///   * normal held-button firing for sword, shield, dagger, crossbow, grenade
///   * press/hold/release weapon input for charge weapons like Bow
///   * weapon damage upgrades from powerups
///   * weapon type IDs so the PowerUp UI can upgrade or replace weapons
/// </summary>
public abstract class Weapon : MonoBehaviour
{
    [Header("Common")]
    [Tooltip("Display name for HUD or debug.")]
    public string weaponName = "Weapon";
    [Tooltip("Icon shown in the HUD weapon slot when this weapon is equipped. Optional — leave empty for a placeholder.")]
    public Sprite hudIcon;
    [Tooltip("Z-rotation applied to the HUD icon (degrees, clockwise). Use this when a rendered icon ends up sideways. Try 0, 90, 180, or -90 first.")]
    public float hudIconRotation = 0f;
    [Tooltip("Scale multiplier applied to the HUD icon. 1 = native size, 0.7 = smaller (good for icons that fill the whole slot edge-to-edge), 1.3 = larger (good for tall thin icons that look small inside the slot).")]
    public float hudIconZoom = 1f;

    [Tooltip("Weapon type used by powerups and replacement UI.")]
    public eWeaponType weaponType = eWeaponType.none;

    [Tooltip("Damage applied per individual hit on an enemy.")]
    public float damage = 25f;

    [Tooltip("Seconds between firings. Subclasses can add extra conditions.")]
    public float cooldown = 0.5f;

    [Tooltip("Layers the weapon's hit detection considers. Set to your Enemy layer.")]
    public LayerMask enemyLayers = ~0;

    [Header("Damage Upgrades")]
    [Tooltip("Current damage upgrade level.")]
    public int damageLevel = 1;

    [Tooltip("Maximum damage upgrade level.")]
    public int maxDamageLevel = 5;

    [Tooltip("How much damage is added each time this weapon gets upgraded.")]
    public float damageIncreasePerLevel = 5f;

    protected float cooldownTimer;

    /// <summary>True when the weapon is allowed to fire right now.</summary>
    public virtual bool CanFire => cooldownTimer <= 0f;

    /// <summary>
    /// 0..1 fraction of remaining cooldown. 1 = just fired (HUD overlay full),
    /// 0 = ready to fire (overlay empty). Drives the cooldown overlay fillAmount in GameHUD.
    /// </summary>
    public float CooldownProgress => cooldown > 0f ? Mathf.Clamp01(cooldownTimer / cooldown) : 0f;

    public bool IsDamageMaxed => damageLevel >= maxDamageLevel;

    [Header("Attack-Speed Boost")]
    [Tooltip("Floor for cooldown when applying AttackSpeed boosts.")]
    public float minCooldown = 0.08f;
    [Tooltip("How much cooldown shrinks per AttackSpeed boost (seconds).")]
    public float cooldownReductionPerBoost = 0.04f;

    // ---- Boost API ----
    // The picked-up weapon defines a BoostKind. The chosen slot's weapon
    // applies that boost to itself. Each weapon overrides DescribeBoost /
    // IsBoostMaxed / TryApplyBoost for the kinds it handles specially. Any
    // kind a weapon doesn't override falls back to +damage (or, for
    // AttackSpeed, the base implementation reduces cooldown for everyone).

    /// <summary>
    /// Maps a picked-up weapon type to the BoostKind it grants.
    /// Bow/Sword/Shield → Damage, Crossbow → Projectiles,
    /// Grenade → Range, Dagger → AttackSpeed.
    /// </summary>
    public static BoostKind GetBoostKindForPickup(eWeaponType pickup)
    {
        switch (pickup)
        {
            case eWeaponType.crossbow: return BoostKind.Projectiles;
            case eWeaponType.grenade:  return BoostKind.Range;
            case eWeaponType.dagger:   return BoostKind.AttackSpeed;
            default:                   return BoostKind.Damage;
        }
    }

    /// <summary>
    /// True if applying <paramref name="kind"/> to this weapon would do nothing
    /// because the relevant stat is already at its cap. The UI uses this to
    /// disable the Boost button (or convert it into a Hero stat boost).
    /// </summary>
    public virtual bool IsBoostMaxed(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.AttackSpeed: return cooldown <= minCooldown + 0.001f;
            default:                    return IsDamageMaxed;
        }
    }

    /// <summary>
    /// Short human-readable label describing exactly what the next boost will
    /// do for THIS weapon (e.g. "+5 Damage", "+1 Projectile", "+15% Attack
    /// Speed"). Shown on the powerup choice UI's Boost button.
    /// </summary>
    public virtual string DescribeBoost(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                float cur = Mathf.Max(minCooldown, cooldown);
                float nxt = Mathf.Max(minCooldown, cooldown - cooldownReductionPerBoost);
                if (nxt >= cur) return "Attack Speed Maxed";
                return $"+{(cur / nxt - 1f) * 100f:0}% Attack Speed";

            default:
                return $"+{damageIncreasePerLevel:0.#} Damage";
        }
    }

    /// <summary>
    /// Apply <paramref name="kind"/> to this weapon. Returns true if the boost
    /// was actually applied; false if the weapon is already at its cap (the
    /// UI can fall back to a Hero stat boost in that case).
    /// </summary>
    public virtual bool TryApplyBoost(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                if (IsBoostMaxed(BoostKind.AttackSpeed)) return false;
                cooldown = Mathf.Max(minCooldown, cooldown - cooldownReductionPerBoost);
                return true;

            default:
                return TryUpgradeDamage();
        }
    }

    protected virtual void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    /// <summary>
    /// Called once on the frame the fire button is pressed.
    /// Default weapons do nothing here. Bow overrides this to begin charging.
    /// </summary>
    public virtual bool OnFireDown(Hero owner)
    {
        return false;
    }

    /// <summary>
    /// Called every frame the fire button is held.
    /// Default weapons auto-repeat through TryFire().
    /// </summary>
    public virtual bool OnFireHeld(Hero owner)
    {
        return TryFire(owner);
    }

    /// <summary>
    /// Called once on the frame the fire button is released.
    /// Default weapons do nothing here. Bow overrides this to release its shot.
    /// </summary>
    public virtual bool OnFireUp(Hero owner)
    {
        return false;
    }

    /// <summary>
    /// Called when the hero takes some action that should cancel this weapon's
    /// in-progress state (e.g. dashing while charging the bow). Default: no-op.
    /// Charging weapons should override and reset their state here.
    /// </summary>
    public virtual void OnInterrupted(Hero owner) { }

    /// <summary>
    /// Try to fire the weapon. Returns true if it actually went off so the caller
    /// (e.g. Hero) can play an attack animation.
    /// Try to fire the weapon. Returns true if it actually went off so the Hero
    /// can play an attack animation.
    /// </summary>
    public bool TryFire(Hero owner)
    {
        if (!CanFire || owner == null)
            return false;

        Fire(owner);
        cooldownTimer = cooldown;
        return true;
    }

    /// <summary>
    /// Attempts to increase this weapon's damage.
    /// Returns false if already maxed, so Hero can convert the pickup into a stat boost.
    /// </summary>
    public bool TryUpgradeDamage()
    {
        if (IsDamageMaxed)
            return false;

        damageLevel++;
        damage += damageIncreasePerLevel;

        Debug.Log($"{weaponName} damage upgraded to level {damageLevel}. Damage is now {damage}.");
        return true;
    }

    /// <summary>
    /// Subclasses spawn slashes, projectiles, beams, etc. here.
    /// Bow can leave this unused because it handles press/hold/release directly.
    /// </summary>
    protected virtual void Fire(Hero owner)
    {
    }
}