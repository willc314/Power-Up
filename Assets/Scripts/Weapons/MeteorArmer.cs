using UnityEngine;

/// <summary>
/// Generic "this projectile/swing was rolled to spawn a meteor on its first
/// enemy hit" marker. Attached to a projectile's GameObject by
/// <see cref="Hero.TryArmMeteorOnProjectile"/> when the Meteor general augment
/// is active, the swing/shot was a crit, AND the per-fire chance roll passed.
///
/// Each individual weapon hit-handler is responsible for calling
/// <see cref="TryConsume"/> on its first enemy hit — semantics: one meteor
/// per fire-event. Subsequent hits in the same fire (e.g. piercing arrows,
/// AOE explosions, multi-tick shield throws) will see the armer disarmed and
/// no-op cleanly.
///
/// This component is independent of the original (now removed) shield-only
/// meteor wiring — see <see cref="ShieldMeteor.Spawn"/> for the actual VFX +
/// AOE pass it kicks off.
/// </summary>
public class MeteorArmer : MonoBehaviour
{
    private float damage;
    private float fallDuration;
    private float aoeRadius;
    private GameObject vfxPrefab;
    private Vector3 vfxRotationOffset;
    private LayerMask hitLayers;
    private bool armed;

    /// <summary>
    /// Bake meteor parameters in and arm the projectile. Called once by the
    /// Hero helper at fire time after the augment + crit + chance gates pass.
    /// <paramref name="vfxRotationOffset"/> is applied to the spawned VFX's
    /// local rotation so prefabs that point along an unusual local axis
    /// (e.g. ppfxRay's +Z trail) can be re-aimed downward without a custom
    /// wrapper prefab.
    /// </summary>
    public void Arm(float damage, float fallDuration, float aoeRadius,
                    GameObject vfxPrefab, Vector3 vfxRotationOffset, LayerMask hitLayers)
    {
        this.damage = damage;
        this.fallDuration = fallDuration;
        this.aoeRadius = aoeRadius;
        this.vfxPrefab = vfxPrefab;
        this.vfxRotationOffset = vfxRotationOffset;
        this.hitLayers = hitLayers;
        armed = true;
    }

    /// <summary>
    /// True if the projectile/swing was rolled for a meteor and hasn't
    /// consumed it yet. Call sites can fast-path skip cheap lookups using
    /// this before formatting an enemy position.
    /// </summary>
    public bool IsArmed => armed;

    /// <summary>
    /// Move the armed state from <paramref name="source"/> to a new
    /// MeteorArmer added to <paramref name="targetGo"/>, then disarm the
    /// source so the original projectile won't fire it. Used by
    /// <see cref="Grenade.Detonate"/> to forward the armer onto the
    /// spawned Explosion (so the meteor is gated on an actual enemy hit
    /// inside the AOE rather than firing at the blast center regardless).
    /// No-op if source is null / unarmed.
    /// </summary>
    public static void Transfer(MeteorArmer source, GameObject targetGo)
    {
        if (source == null || !source.armed || targetGo == null) return;
        var dest = targetGo.GetComponent<MeteorArmer>();
        if (dest == null) dest = targetGo.AddComponent<MeteorArmer>();
        dest.Arm(
            damage:            source.damage,
            fallDuration:      source.fallDuration,
            aoeRadius:         source.aoeRadius,
            vfxPrefab:         source.vfxPrefab,
            vfxRotationOffset: source.vfxRotationOffset,
            hitLayers:         source.hitLayers);
        source.armed = false;
    }

    /// <summary>
    /// Spawn the meteor at <paramref name="targetPos"/> and disarm. No-op if
    /// the armer was never armed or has already been consumed. Returns true
    /// only when a meteor was actually spawned (useful for log + debug).
    /// </summary>
    public bool TryConsume(Vector3 targetPos)
    {
        if (!armed) return false;
        armed = false;
        ShieldMeteor.Spawn(
            targetPos:         targetPos,
            damage:            damage,
            fallDuration:      fallDuration,
            aoeRadius:         aoeRadius,
            vfxPrefab:         vfxPrefab,
            vfxRotationOffset: vfxRotationOffset,
            hitLayers:         hitLayers);
        return true;
    }
}
