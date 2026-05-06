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
    private float shakeAmplitude;
    private float shakeDuration;
    private AudioClip impactSound;
    private float impactSoundVolume;
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
        float vfxScale,
        float shakeAmplitude,
        float shakeDuration,
        AudioClip impactSound,
        float impactSoundVolume,
        LayerMask hitLayers)
    {
        var go = new GameObject("ShieldMeteor");
        var m = go.AddComponent<ShieldMeteor>();
        m.targetPos = targetPos;
        m.damage = damage;
        m.aoeRadius = Mathf.Max(0f, aoeRadius);
        m.fallDuration = Mathf.Max(0.05f, fallDuration);
        m.shakeAmplitude = Mathf.Max(0f, shakeAmplitude);
        m.shakeDuration = Mathf.Max(0f, shakeDuration);
        m.impactSound = impactSound;
        m.impactSoundVolume = Mathf.Clamp01(impactSoundVolume);
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

        // One-shot spawn SFX at the impact point — plays at the moment the
        // visual starts falling so the audio arrives BEFORE the impact
        // (matching the falling-meteor "incoming!" cue most packs author
        // their sound around). Routed through SoundManager so it picks up
        // the global SFX volume + 3D rolloff.
        //
        // Pitch-scaled so the clip's full length matches the meteor's
        // fallDuration: the sound starts at spawn AND ends right at the
        // impact moment, regardless of whether the clip is shorter or
        // longer than the fall. Unity's pitch knob time-stretches without
        // cropping (pitch>1 = faster+higher, pitch<1 = slower+lower) and
        // the trade-off is a tonal shift, which the user accepted as the
        // alternative to cutting the clip short.
        if (impactSound != null && SoundManager.Instance != null)
        {
            float pitch = 1f;
            if (impactSound.length > 0.0001f && m.fallDuration > 0.0001f)
            {
                // Clamp to Unity's documented AudioSource.pitch range
                // [-3, 3]. Floor at a small positive to avoid divide-by-
                // zero / negative-pitch (reverse playback) edge cases.
                pitch = Mathf.Clamp(impactSound.length / m.fallDuration, 0.05f, 3f);
            }
            SoundManager.Instance.PlaySfxAt(impactSound, targetPos, m.impactSoundVolume, pitch);
        }

        // Spawn the visual as a child so its lifetime is bound to the host.
        if (vfxPrefab != null)
        {
            var vfx = Instantiate(vfxPrefab, go.transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localRotation = Quaternion.Euler(vfxRotationOffset);
            // Uniform scale from Hero.meteorVfxScale — most ray / lightning
            // prefabs ship at a size sized for an event-VFX setpiece (huge
            // and screen-filling); a multiplier <1 shrinks them to a
            // weapon-augment-appropriate size. Force every ParticleSystem
            // to Hierarchy scaling mode FIRST — many packs (ParticleProFX
            // included) ship with scalingMode = Local or Shape, which
            // ignores transform scale entirely and is why setting scale
            // on the prefab directly does nothing visible. Hierarchy mode
            // makes localScale actually shrink the particles.
            VfxHelpers.ForceHierarchyScaling(vfx);
            float s = Mathf.Max(0.0001f, vfxScale);
            vfx.transform.localScale = vfx.transform.localScale * s;
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

        // Camera shake for impact feel. Amplitude / duration come from
        // Hero.meteorShakeAmplitude / meteorShakeDuration so the player
        // can tune the punchiness without recompiling — pass 0 amplitude
        // to disable shake entirely.
        if (shakeAmplitude > 0f && shakeDuration > 0f && Camera.main != null)
        {
            var cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null) cam.Shake(shakeAmplitude, shakeDuration);
        }

        // (Spawn SFX is fired at Spawn() time now, not Impact — the audio
        // arrives at the start of the fall instead of after damage lands.)

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
