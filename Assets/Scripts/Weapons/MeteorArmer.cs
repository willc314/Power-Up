using UnityEngine;

/// <summary>
/// Per-fire-event meteor data + a shared "consumed" flag. One instance is
/// created per crit fire that passes the chance roll, then the SAME instance
/// is referenced by every projectile in the volley (single-shot weapons,
/// fanned crossbow / bow shots, AOE explosions, etc). Whichever projectile
/// lands the first enemy hit consumes the roll for the entire fire-event,
/// preventing duplicate meteors AND ensuring no meteor is "wasted" on a
/// projectile that misses while its siblings hit.
///
/// Semantics are still "one meteor per fire-event"; the shared flag just
/// makes that bookkeeping work across multiple projectiles instead of
/// betting all the hit-coverage on one specific arrow.
/// </summary>
public class MeteorRoll
{
    public float damage;
    public float fallDuration;
    public float aoeRadius;
    public GameObject vfxPrefab;
    public Vector3 vfxRotationOffset;
    public float vfxScale;
    public float shakeAmplitude;
    public float shakeDuration;
    public AudioClip impactSound;
    public float impactSoundVolume;
    public LayerMask hitLayers;
    public bool consumed;
}

/// <summary>
/// Generic "this projectile/swing was rolled to spawn a meteor on its first
/// enemy hit" marker. Attached to a projectile's GameObject by
/// <see cref="Hero.TryArmMeteorOnProjectile"/> when the Meteor general augment
/// is active, the swing/shot was a crit, AND the per-fire chance roll passed.
///
/// Each individual weapon hit-handler is responsible for calling
/// <see cref="TryConsume"/> on its first enemy hit — semantics: one meteor
/// per fire-event. The armer holds a reference to a shared
/// <see cref="MeteorRoll"/>, so multi-projectile weapons (crossbow fan,
/// bow fan) can attach the SAME roll to every arrow and have any arrow's
/// first hit fire the meteor — the shared `consumed` flag ensures it only
/// fires once.
///
/// See <see cref="ShieldMeteor.Spawn"/> for the actual VFX + AOE pass it
/// kicks off.
/// </summary>
public class MeteorArmer : MonoBehaviour
{
    private MeteorRoll roll;

    /// <summary>
    /// Bind this armer to a shared <see cref="MeteorRoll"/>. Multiple
    /// armers (one per projectile in a fanned volley) typically share the
    /// same roll so first-hit-wins works across every arrow.
    /// </summary>
    public void Arm(MeteorRoll roll)
    {
        this.roll = roll;
    }

    /// <summary>
    /// True if this armer is bound to an unconsumed roll. Used by
    /// <see cref="Grenade.Detonate"/> as a fast-path before forwarding the
    /// roll to the spawned Explosion.
    /// </summary>
    public bool IsArmed => roll != null && !roll.consumed;

    /// <summary>
    /// Forward this armer's roll onto a fresh MeteorArmer added to
    /// <paramref name="targetGo"/>. Used by <see cref="Grenade.Detonate"/>
    /// to hand off the meteor opportunity from the in-flight grenade to the
    /// spawned Explosion, so the meteor is gated on an actual enemy hit
    /// inside the AOE rather than firing at the blast center regardless.
    /// No-op if source is null / already consumed.
    /// </summary>
    public static void Transfer(MeteorArmer source, GameObject targetGo)
    {
        if (source == null || source.roll == null || source.roll.consumed) return;
        if (targetGo == null) return;
        var dest = targetGo.GetComponent<MeteorArmer>();
        if (dest == null) dest = targetGo.AddComponent<MeteorArmer>();
        dest.Arm(source.roll);
        // Source's reference is intentionally KEPT — the shared `consumed`
        // flag is what gates double-fire, not whether the source still
        // holds a reference. Letting the source point at the same roll is
        // harmless (it just sees consumed = true after dest fires).
    }

    /// <summary>
    /// Spawn the meteor at <paramref name="targetPos"/> and mark the shared
    /// roll consumed so sibling armers in the same fire-event don't
    /// double-fire. No-op if there's no roll bound or if the roll has
    /// already been consumed by a sibling. Returns true only when a meteor
    /// was actually spawned.
    /// </summary>
    public bool TryConsume(Vector3 targetPos)
    {
        if (roll == null || roll.consumed) return false;
        roll.consumed = true;
        ShieldMeteor.Spawn(
            targetPos:         targetPos,
            damage:            roll.damage,
            fallDuration:      roll.fallDuration,
            aoeRadius:         roll.aoeRadius,
            vfxPrefab:         roll.vfxPrefab,
            vfxRotationOffset: roll.vfxRotationOffset,
            vfxScale:          roll.vfxScale,
            shakeAmplitude:    roll.shakeAmplitude,
            shakeDuration:     roll.shakeDuration,
            impactSound:       roll.impactSound,
            impactSoundVolume: roll.impactSoundVolume,
            hitLayers:         roll.hitLayers);
        return true;
    }
}
