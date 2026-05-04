using System.Collections;
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

    /// <summary>Damage value at scene start, captured by Start(). Used by the StatsMenu for original-vs-current display.</summary>
    public float OriginalDamage   { get; private set; }
    /// <summary>Cooldown value at scene start, captured by Start(). Used by the StatsMenu.</summary>
    public float OriginalCooldown { get; private set; }

    private bool originalsCaptured;
    /// <summary>
    /// Snapshot the inspector-provided damage and cooldown so the StatsMenu
    /// can show what the player started with versus where they are now.
    /// Uses Start() instead of Awake() so any subclass tweaks during Awake
    /// (e.g. DaggerWeapon's setting weaponName/type) are already in effect.
    /// </summary>
    private void Start()
    {
        if (originalsCaptured) return;
        originalsCaptured = true;
        OriginalDamage   = damage;
        OriginalCooldown = cooldown;
    }

    /// <summary>
    /// Override per weapon to expose upgrade-tracked stats (multi-arrow,
    /// blade size, beam bonuses, etc.) as a multi-line string the StatsMenu
    /// renders verbatim. Default: empty (no extras).
    /// </summary>
    public virtual string GetExtraStatsBlock()
    {
        return "";
    }

    [Header("Attack-Speed Boost")]
    [Tooltip("Floor for cooldown when applying AttackSpeed boosts.")]
    public float minCooldown = 0.08f;
    [Tooltip("Attack speed gained on the FIRST AttackSpeed boost, as a fraction. 0.15 = +15% attack speed for boost #1; subsequent boosts are scaled down by attackSpeedDiminishingFactor each.")]
    [Range(0f, 1f)] public float attackSpeedIncreasePercent = 0.15f;
    [Tooltip("Each AttackSpeed boost is this fraction as effective as the previous. 0.85 = boost #2 gives 85% of #1's gain, #3 gives 72%, #4 gives 61%, etc.")]
    [Range(0f, 1f)] public float attackSpeedDiminishingFactor = 0.85f;
    [Tooltip("Floor on the diminishing curve. The per-boost attack-speed gain never shrinks below this — so a fully-stacked player still gets a small but non-zero gain per pickup. 0.01 = +1% attack speed minimum.")]
    [Range(0f, 1f)] public float minAttackSpeedIncreasePercent = 0.01f;

    /// <summary>How many AttackSpeed boosts have been applied. Drives the per-boost diminishing curve.</summary>
    protected int attackSpeedBoostsTaken = 0;

    /// <summary>
    /// Diminishing multiplier the next AttackSpeed boost is scaled by. Floored
    /// so the effective per-boost percent doesn't drop below
    /// minAttackSpeedIncreasePercent — i.e. the player always gets at least
    /// that much per pickup, no matter how stacked they are.
    /// </summary>
    protected float AttackSpeedDiminisher
    {
        get
        {
            float raw = Mathf.Pow(attackSpeedDiminishingFactor, attackSpeedBoostsTaken);
            if (attackSpeedIncreasePercent > 0.0001f)
            {
                float minRatio = minAttackSpeedIncreasePercent / attackSpeedIncreasePercent;
                return Mathf.Max(minRatio, raw);
            }
            return raw;
        }
    }

    [Header("Post-Max Scaling")]
    [Tooltip("After the weapon hits its main cap, further boosts still apply but at this fraction of normal strength. 0.5 = half-strength, forever.")]
    [Range(0f, 1f)] public float postMaxBoostScale = 0.5f;

    [Header("Extra Attacks (Projectile boost on melee/thrown)")]
    [Tooltip("How many extra Fire() calls happen after each successful TryFire. Spacing is derived from cooldown so all extras complete before the next normal attack. Bumped by the Projectiles boost on Sword/Shield/Dagger/Grenade.")]
    public int extraAttackCount = 0;
    [Tooltip("Cap on extraAttackCount. Past this, Projectiles boosts fall back to scaled damage.")]
    public int maxExtraAttackCount = 4;
    [Tooltip("Lower bound on the time between extra attacks. Used when cooldown / (extraAttackCount + 1) would otherwise be too small to be visible.")]
    public float extraAttackMinSpacing = 0.05f;

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
    ///
    /// When the weapon is past its cap (IsBoostMaxed returns true), the
    /// description and applied magnitude both shrink by postMaxBoostScale,
    /// so the UI always shows the actual delta the player will get.
    /// </summary>
    public virtual string DescribeBoost(BoostKind kind)
    {
        float scale = IsBoostMaxed(kind) ? postMaxBoostScale : 1f;
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                if (cooldown <= minCooldown + 0.001f)
                {
                    // At the hard floor — boost falls back to scaled damage.
                    return $"+{damageIncreasePerLevel * scale:0.#} Damage";
                }
                float cur = Mathf.Max(minCooldown, cooldown);
                // % attack speed → cooldown shrinks by 1/(1+pct), with the
                // current diminisher applied so the player sees the actual
                // gain (which decreases each pickup).
                float effPct = attackSpeedIncreasePercent * AttackSpeedDiminisher * scale;
                float nxt = Mathf.Max(minCooldown, cooldown / (1f + effPct));
                if (nxt >= cur) return "Attack Speed Maxed";
                return $"+{(cur / nxt - 1f) * 100f:0}% Attack Speed";

            default:
                return $"+{damageIncreasePerLevel * scale:0.#} Damage";
        }
    }

    /// <summary>
    /// Apply <paramref name="kind"/> to this weapon. Always returns true: even
    /// past the cap, the boost still applies, just at postMaxBoostScale of its
    /// normal magnitude (so the player can keep stacking smaller upgrades).
    /// </summary>
    public virtual bool TryApplyBoost(BoostKind kind)
    {
        float scale = IsBoostMaxed(kind) ? postMaxBoostScale : 1f;
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                if (cooldown <= minCooldown + 0.001f)
                {
                    // At hard floor — convert the boost into scaled damage.
                    damage     += damageIncreasePerLevel * scale;
                    damageLevel++;
                    return true;
                }
                // % reduction in cooldown → cooldown / (1 + pct), with the
                // diminisher applied so each subsequent boost is weaker.
                {
                    float pct = attackSpeedIncreasePercent * AttackSpeedDiminisher * scale;
                    cooldown = Mathf.Max(minCooldown, cooldown / (1f + pct));
                    attackSpeedBoostsTaken++;
                }
                return true;

            default:
                damage     += damageIncreasePerLevel * scale;
                damageLevel++;
                return true;
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

        if (extraAttackCount > 0)
            StartCoroutine(FireExtras(owner));

        return true;
    }

    /// <summary>
    /// Coroutine that fires extraAttackCount additional Fire(owner) calls
    /// evenly spread across the weapon's cooldown so that all of them
    /// complete before the next normal attack is allowed. Spacing is derived
    /// from the current cooldown — faster weapons fire their extras faster.
    /// </summary>
    private IEnumerator FireExtras(Hero owner)
    {
        // Snapshot count + cooldown so live mutations during the sequence
        // (a boost picked up mid-run, etc.) don't change the schedule.
        int n = extraAttackCount;
        float window = cooldown;
        // Spread N extras over the cooldown: spacing = window / (N + 1) puts
        // the last extra at N*window/(N+1) < window, so the next normal Fire
        // can fire on schedule without overlap.
        float spacing = window > 0f
            ? Mathf.Max(extraAttackMinSpacing, window / (n + 1))
            : Mathf.Max(extraAttackMinSpacing, 0.1f);

        for (int i = 0; i < n; i++)
        {
            yield return new WaitForSeconds(spacing);
            // Bail out if anything has gone away mid-sequence.
            if (this == null || owner == null || owner.IsDead) yield break;
            Fire(owner);
        }
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