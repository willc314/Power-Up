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
    [Tooltip("Damage applied per individual hit on an enemy.")]
    public float damage = 25f;
    [Tooltip("Seconds between firings. Subclasses can add extra conditions (e.g. shield must return first).")]
    public float cooldown = 0.5f;
    [Tooltip("Layers the weapon's hit detection considers. Set to your 'Enemy' layer.")]
    public LayerMask enemyLayers = ~0;

    protected float cooldownTimer;

    /// <summary>True when the weapon is allowed to fire right now.</summary>
    public virtual bool CanFire => cooldownTimer <= 0f;

    protected virtual void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
    }

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

    /// <summary>Subclasses spawn slashes / projectiles / etc. here.</summary>
    protected abstract void Fire(Hero owner);
}
