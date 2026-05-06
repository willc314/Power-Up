using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-shot AOE damage burst at this transform's position. Damages every enemy
/// in radius once (also applies friendly-fire damage to the hero if hitsHero is on).
/// Optionally spawns a particle effect prefab for visuals.
/// Self-destroys after the longer of: vfxLifetime or instant.
/// </summary>
public class Explosion : MonoBehaviour
{
    [Header("AOE")]
    [Tooltip("Radius of the explosion.")]
    public float radius = 6f;
    [Tooltip("Damage applied to each enemy inside the radius.")]
    public float damage = 60f;
    [Tooltip("Layers the explosion checks for damage. Set to your Enemy layer (and Player if you want self-damage).")]
    public LayerMask hitLayers = ~0;
    [Tooltip("If true, the hero also takes damage if inside the radius.")]
    public bool hitsHero = true;

    /// <summary>
    /// Optional override for friendly-fire damage applied to the hero. Set by
    /// GrenadeWeapon to the player's pre-crit attack damage so the player's
    /// crit roll doesn't amplify their own self-damage. If &lt;= 0 the
    /// original behavior (damage * 0.5f) applies.
    /// </summary>
    [System.NonSerialized] public float friendlyFireDamage = 0f;

    [Header("VFX")]
    [Tooltip("Optional particle prefab spawned at the blast center. For something fancier than the built-in cube burst.")]
    public GameObject vfxPrefab;
    [Tooltip("Seconds to wait before destroying this object (leave time for the VFX to play).")]
    public float vfxLifetime = 2f;
    [Tooltip("Reference radius the vfxPrefab was authored at. The spawned VFX is uniformly scaled by (current radius / this value), so a grenade Range upgrade that doubles the AOE radius also doubles the visual size. Default 6 matches the Explosion's default radius — set this to whatever radius your VFX prefab visually 'fills' at and the scaling will line up. Set to 0 to disable size scaling.")]
    public float vfxRefRadius = 6f;

    [Header("Built-in Debris Burst")]
    [Tooltip("If true, spawns a quick burst of cube/sphere debris flying upward and outward — looks like an explosion without needing a particle prefab.")]
    public bool useDebrisBurst = true;
    [Tooltip("Number of debris particles in the burst.")]
    public int debrisCount = 24;
    [Tooltip("Color of the debris particles. Orange/red reads as fire.")]
    public Color debrisColor = new Color(1f, 0.5f, 0.1f);
    [Tooltip("Initial speed of each debris particle.")]
    public float debrisSpeed = 9f;
    [Tooltip("Edge length of each particle.")]
    public float debrisSize = 0.22f;
    [Tooltip("Seconds before each particle self-destroys.")]
    public float debrisLifetime = 0.9f;
    [Tooltip("Spread cone angle from straight-up. 90 = full hemisphere upward, 180 = full sphere.")]
    public float debrisSpread = 90f;
    [Tooltip("Vertical offset above the explosion center where particles spawn.")]
    public float debrisHeight = 0.3f;

    [Header("Camera")]
    [Tooltip("Camera shake amplitude on detonate.")]
    public float shakeAmplitude = 0.4f;
    [Tooltip("Camera shake duration on detonate.")]
    public float shakeDuration = 0.25f;

    [Header("Debug")]
    [Tooltip("Log to the console how many enemies were hit by this explosion (useful for setup verification).")]
    public bool debugLog = true;

    private static readonly Collider[] hitBuffer = new Collider[64];

    public void Detonate()
    {
        // Damage pass.
        HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
        int n = Physics.OverlapSphereNonAlloc(transform.position, radius, hitBuffer, hitLayers, QueryTriggerInteraction.Collide);
        bool heroHit = false;
        // Meteor general augment: if our spawner (Grenade) transferred a
        // MeteorArmer onto us, fire it on the FIRST enemy hit. Mirrors the
        // "one meteor per fire-event, gated on enemy hit" semantics every
        // other weapon already follows.
        var meteorArmer = GetComponent<MeteorArmer>();
        for (int i = 0; i < n; i++)
        {
            Enemy e = hitBuffer[i].GetComponentInParent<Enemy>();
            if (e != null && hitEnemies.Add(e))
            {
                e.TakeDamage(damage);
                if (meteorArmer != null) meteorArmer.TryConsume(e.transform.position);
                continue;
            }
            if (hitsHero && !heroHit)
            {
                Hero h = hitBuffer[i].GetComponentInParent<Hero>();
                if (h != null)
                {
                    // Friendly-fire damage uses the pre-crit value when the
                    // weapon supplied one — keeps the player's crit roll
                    // from amplifying their own self-damage. Falls back to
                    // the legacy `damage * 0.5f` if the source didn't set
                    // an override (so non-grenade callers still work).
                    float heroDamage = friendlyFireDamage > 0f ? friendlyFireDamage : damage * 0.5f;
                    h.TakeDamage(heroDamage);
                    heroHit = true;
                }
            }
        }

        if (debugLog)
            Debug.Log($"Explosion at {transform.position}: overlapped {n} colliders → damaged {hitEnemies.Count} enemies, hero hit: {heroHit}.", this);

        // VFX.
        if (vfxPrefab != null)
        {
            GameObject vfx = Instantiate(vfxPrefab, transform.position, Quaternion.identity);
            // Tame the prefab: kill physics interference so it can't push
            // mobs around, AND force ParticleSystems to non-looping so
            // a third-party prefab with main.loop = true doesn't keep
            // replaying its blast animation indefinitely. The Destroy()
            // schedule below handles cleanup once the burst finishes.
            VfxHelpers.DisablePhysicsInterference(vfx);
            VfxHelpers.ConfigureAsOneShotVfx(vfx);
            // Scale the GROUND-CRACK / mesh parts of the VFX to match the
            // AOE radius, but keep particles (sparks, smoke, fire) at
            // their authored size. A 2× radius boost grows the ground
            // crack to 2× but the sparks shouldn't read as chunky-twice-
            // as-big. ScaleStaticRenderersOnly walks non-particle
            // renderers and scales their transforms while pinning every
            // ParticleSystem to Local scaling so it ignores any parent
            // transform changes.
            if (vfxRefRadius > 0.0001f)
            {
                float scale = Mathf.Max(0.0001f, radius / vfxRefRadius);
                VfxHelpers.ScaleStaticRenderersOnly(vfx, scale);
            }
            Destroy(vfx, vfxLifetime);
        }

        if (useDebrisBurst)
        {
            HitParticles.EmitBurst(
                origin: transform.position + Vector3.up * debrisHeight,
                direction: Vector3.up,
                count: debrisCount,
                speed: debrisSpeed,
                lifetime: debrisLifetime,
                size: debrisSize,
                color: debrisColor,
                spreadAngle: debrisSpread,
                useGravity: true);
        }

        // Camera shake.
        if (shakeAmplitude > 0f && CameraFollowInstance() is CameraFollow cam)
            cam.Shake(shakeAmplitude, shakeDuration);

        Destroy(gameObject, 0.05f);
    }

    private static CameraFollow CameraFollowInstance()
    {
        if (Camera.main == null) return null;
        return Camera.main.GetComponent<CameraFollow>();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
