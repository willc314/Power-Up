using UnityEngine;

/// <summary>
/// Abstract base class for all weapons.
/// A weapon is a MonoBehaviour you put on the Hero (or a child of the Hero).
/// The Hero references one as Primary (left click) and one as Secondary (right click).
///
/// Each subclass decides what happens on Fire(): spawn a slash visual, throw a
/// projectile, summon an aura, etc. The base class handles cooldown.
/// </summary>
public abstract class Weapon : MonoBehaviour
{
    [Header("Common")]
    [Tooltip("Display name for HUD or debug.")]
    public string weaponName = "Weapon";
    [Tooltip("Icon shown in the HUD weapon slot when this weapon is equipped. Optional — leave empty for a placeholder.")]
    public Sprite hudIcon;
    [Tooltip("Damage applied per individual hit on an enemy.")]
    public float damage = 25f;
    [Tooltip("Seconds between firings. Subclasses can add extra conditions (e.g. shield must return first).")]
    public float cooldown = 0.5f;
    [Tooltip("Layers the weapon's hit detection considers. Set to your 'Enemy' layer.")]
    public LayerMask enemyLayers = ~0;

    protected float cooldownTimer;

    /// <summary>True when the weapon is allowed to fire right now.</summary>
    public virtual bool CanFire => cooldownTimer <= 0f;

    /// <summary>
    /// 0 when the weapon is ready to fire, 1 when the cooldown has just been
    /// reset. Useful for HUD radial fills / cooldown overlays.
    /// </summary>
    public float CooldownProgress
    {
        get
        {
            if (cooldown <= 0f) return 0f;
            return Mathf.Clamp01(cooldownTimer / cooldown);
        }
    }

    protected virtual void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
    }

    // ---- Input lifecycle (called from Hero each frame) ----
    // Default behavior: weapons auto-repeat fire while the button is held (existing
    // sword/shield/dagger/crossbow/grenade behavior). Bow overrides all three to
    // implement charging.

    /// <summary>Called once on the frame the fire button is pressed. Return true to play attack anim.</summary>
    public virtual bool OnFireDown(Hero owner) { return false; }

    /// <summary>Called every frame the fire button is held. Default: try to fire (auto-repeat).</summary>
    public virtual bool OnFireHeld(Hero owner) { return TryFire(owner); }

    /// <summary>Called once on the frame the fire button is released. Return true to play attack anim.</summary>
    public virtual bool OnFireUp(Hero owner) { return false; }

    /// <summary>
    /// Called when the hero takes some action that should cancel this weapon's
    /// in-progress state (e.g. dashing while charging the bow). Default: no-op.
    /// Charging weapons should override and reset their state here.
    /// </summary>
    public virtual void OnInterrupted(Hero owner) { }

    /// <summary>
    /// Try to fire the weapon. Returns true if it actually went off so the caller
    /// (e.g. Hero) can play an attack animation.
    /// </summary>
    public bool TryFire(Hero owner)
    {
        if (!CanFire || owner == null) return false;
        Fire(owner);
        cooldownTimer = cooldown;
        return true;
    }

    /// <summary>Subclasses spawn slashes / projectiles / etc. here. Optional — weapons that don't use the cooldown/Fire pattern (e.g. Bow) can leave this as no-op.</summary>
    protected virtual void Fire(Hero owner) { }
}
