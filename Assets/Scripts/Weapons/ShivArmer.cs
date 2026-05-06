using UnityEngine;

/// <summary>
/// Per-fire marker attached to dagger projectiles / stabs by
/// <see cref="DaggerWeapon"/> while the Elemental Shiv augment is active.
/// Holds a reference back to the weapon so each enemy hit can spawn the
/// appropriate clone fan with the weapon's current config — keeps the
/// hit-handler code (DaggerStab, Projectile) generic without dagger-only
/// branches.
///
/// Unlike <see cref="MeteorArmer"/>, the shiv DOES NOT disarm on use:
/// every enemy hit by this projectile / stab spawns its own fan of clones
/// (semantics: "the dagger was elemental for this hit", not "this fire-event
/// gets one shiv proc"). For piercing daggers / multi-hit stabs this means
/// each hit enemy gets clones, which is the intended augment fantasy.
/// </summary>
public class ShivArmer : MonoBehaviour
{
    private DaggerWeapon dagger;
    private float baseDamage;

    public void Arm(DaggerWeapon dagger, float baseDamage)
    {
        this.dagger = dagger;
        this.baseDamage = baseDamage;
    }

    /// <summary>
    /// Spawn a fan of <see cref="ElementalShivClone"/>s targeting
    /// <paramref name="target"/>. No-op if the dagger reference has been
    /// destroyed or the target died between the parent hit and this call.
    /// </summary>
    public void TriggerOn(Enemy target)
    {
        if (dagger == null || target == null || target.IsDead) return;
        dagger.SpawnElementalCloneFanOn(target, baseDamage);
    }
}
