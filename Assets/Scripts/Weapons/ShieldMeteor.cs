using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-shot falling meteor triggered by the Shield Meteor augment when a
/// crit shield-throw lands a hit. Spawns at a random angle high above the
/// target, animates down to the impact point over a configurable delay,
/// then deals AOE damage to every enemy in radius and despawns.
///
/// Visual is a particle prefab (e.g. GabrielAguiarProductions
/// vfx_MeteorRain_01) instantiated as a child of this host GameObject —
/// the host's transform handles the high-altitude → impact translation,
/// and the prefab's particles render the meteor itself.
///
/// Spawn() is the only public entry point. The host self-destroys after
/// the impact AOE plus a short tail so the prefab's lingering particles
/// (smoke / shockwave) finish playing.
/// </summary>
public class ShieldMeteor : MonoBehaviour
{
    private Vector3 targetPos;
    private float fallDuration;
    private float damage;
    private float aoeRadius;
    private LayerMask hitLayers;
    private float vfxLingerAfterImpact = 1.5f;

    private float t;
    private bool impacted;

    private static readonly Collider[] hitBuffer = new Collider[64];

    /// <summary>
    /// Fire-and-forget meteor at <paramref name="targetPos"/>. The VFX
    /// prefab is spawned IN PLACE at the target — it owns the "fall from
    /// sky" visual via its own particle system, while this host just
    /// waits <paramref name="fallDuration"/> seconds before applying the
    /// AOE damage at the impact point. Tweening the host through the
    /// air doesn't work with most meteor VFX prefabs (their particles
    /// have short world-space lifetimes and die before reaching the
    /// ground), so we let the prefab handle its own animation timing.
    ///
    /// Returns the host so callers can stash a reference if needed.
    /// </summary>
    public static ShieldMeteor Spawn(
        Vector3 targetPos,
        float damage,
        float fallDuration,
        float aoeRadius,
        GameObject vfxPrefab,
        Vector3 vfxRotationOffset,
        LayerMask hitLayers)
    {
        var go = new GameObject("ShieldMeteor");
        var m = go.AddComponent<ShieldMeteor>();
        m.targetPos = targetPos;
        m.damage = damage;
        m.aoeRadius = Mathf.Max(0f, aoeRadius);
        m.fallDuration = Mathf.Max(0.05f, fallDuration);
        m.hitLayers = hitLayers;

        // Place the host at the target. The VFX prefab plays its full
        // "meteor falls and explodes here" animation from this point —
        // no host transform tweening required.
        go.transform.position = targetPos;
        // Random Y rotation so the baked meteor trail in the prefab
        // appears to approach from a different angle each spawn. The
        // VFX child's localRotation = vfxRotationOffset re-aligns
        // prefabs that point along an unusual local axis (most ray /
        // beam prefabs trail along their local +Z, so a (90,0,0) offset
        // re-aims that down to world -Y). The random Y on the host
        // then orbits around the world-vertical fall axis, preserving
        // the downward direction while still varying approach angle.
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Spawn the visual as a child so its lifetime is bound to the host.
        if (vfxPrefab != null)
        {
            var vfx = Instantiate(vfxPrefab, go.transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localRotation = Quaternion.Euler(vfxRotationOffset);
            // Strip every physics-y component that could shove the boss /
            // mobs away as the meteor lands on top of them. Some packs
            // ship ParticleSystem.collision modules that apply forces to
            // rigidbodies in range — disabling those (and any rigidbodies
            // / colliders on the prefab itself) makes the meteor purely
            // visual until the AOE damage pass runs.
            VfxHelpers.DisablePhysicsInterference(vfx);
        }

        return m;
    }

    private void Update()
    {
        if (impacted) return;
        t += Time.deltaTime;
        // Damage AOE applies once fallDuration elapses — synchronized
        // with the prefab's natural impact moment (tune the prefab's
        // particle delays so the visual "lands" at the same time, then
        // tune fallDuration here to match).
        if (t >= fallDuration) Impact();
    }

    private void Impact()
    {
        impacted = true;
        transform.position = targetPos;

        // AOE damage pass — sphere check around the impact point for
        // every enemy in radius. Each enemy hit at most once per meteor.
        var seen = new HashSet<Enemy>();
        int n = Physics.OverlapSphereNonAlloc(targetPos, aoeRadius, hitBuffer, hitLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var e = hitBuffer[i].GetComponentInParent<Enemy>();
            if (e != null && seen.Add(e))
                e.TakeDamage(damage);
        }

        // Camera shake for impact feel — borrows the shake pattern other
        // weapons use. Cheap.
        if (Camera.main != null)
        {
            var cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null) cam.Shake(0.45f, 0.3f);
        }

        // Linger so the prefab's particles finish their post-impact
        // dissipation (smoke, shockwave, etc) before the host vanishes.
        Destroy(gameObject, vfxLingerAfterImpact);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(targetPos, aoeRadius);
    }
}
