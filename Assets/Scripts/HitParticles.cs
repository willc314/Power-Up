using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tiny utility for spawning a quick burst of "particles" — small unlit
/// spheres with rigidbodies that fly off in a cone and self-destruct.
/// They share one unlit material (no shading variation, no shadows) and
/// are colored per-instance via MaterialPropertyBlock so we don't allocate
/// a Material per particle.
///
/// Late-game performance: a global cap on the number of simultaneously
/// active particles prevents lag when attack speed + projectile counts
/// push the spawn rate too high. When a burst would push the live count
/// past the cap, the burst is shrunk to fit (or skipped entirely).
/// </summary>
public static class HitParticles
{
    private static Material sharedMaterial;
    private static MaterialPropertyBlock mpb;
    private static readonly int kBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int kColor     = Shader.PropertyToID("_Color");

    /// <summary>
    /// Maximum live particles allowed on screen at once. Bursts that would
    /// exceed this are truncated. Tune up if hits look too sparse, down if
    /// the game lags during heavy combat.
    /// </summary>
    public static int MaxActiveParticles = 300;

    /// <summary>Live count of particles spawned by this utility.</summary>
    public static int ActiveCount => activeCount;
    private static int activeCount;

    public static void EmitBurst(
        Vector3 origin,
        Vector3 direction,
        int count = 8,
        float speed = 5f,
        float lifetime = 0.5f,
        float size = 0.15f,
        Color? color = null,
        float spreadAngle = 25f,
        bool useGravity = true)
    {
        if (count <= 0) return;

        // How many of the requested particles can we actually spawn before we
        // hit the cap? If we're already maxed out, the whole burst is skipped.
        int budget = Mathf.Max(0, MaxActiveParticles - activeCount);
        if (budget <= 0) return;
        int actualCount = Mathf.Min(count, budget);

        Color c = color ?? Color.white;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
        Vector3 axis = direction.normalized;

        Material mat = GetSharedMaterial();
        if (mpb == null) mpb = new MaterialPropertyBlock();

        for (int i = 0; i < actualCount; i++)
        {
            // Sphere reads as a flat dot from any camera angle (no visible edges like a cube).
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.name = "HitParticle";
            p.transform.position = origin;
            p.transform.localScale = Vector3.one * size;

            // No physics collisions — particles should pass through everything.
            Object.Destroy(p.GetComponent<Collider>());

            // Replace the auto-created lit material with one shared unlit material.
            // Color is applied per-instance via MaterialPropertyBlock so we don't allocate per particle.
            var renderer = p.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            mpb.Clear();
            mpb.SetColor(kBaseColor, c);
            mpb.SetColor(kColor, c);
            renderer.SetPropertyBlock(mpb);

            // Velocity inside a cone around `axis`.
            var rb = p.AddComponent<Rigidbody>();
            rb.useGravity = useGravity;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            rb.velocity = RandomDirInCone(axis, spreadAngle) * speed * Random.Range(0.7f, 1.2f);

            // Track this particle in the live count. The lifecycle component
            // decrements the counter when the GameObject is destroyed.
            p.AddComponent<HitParticleLifecycle>();
            activeCount++;

            Object.Destroy(p, lifetime);
        }
    }

    /// <summary>Called by HitParticleLifecycle.OnDestroy to free a slot in the active count.</summary>
    internal static void NotifyDestroyed()
    {
        if (activeCount > 0) activeCount--;
    }

    private static Material GetSharedMaterial()
    {
        if (sharedMaterial != null) return sharedMaterial;
        // Try URP unlit first, then built-in unlit, then anything that draws.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        sharedMaterial = new Material(shader);
        sharedMaterial.hideFlags = HideFlags.HideAndDontSave;
        return sharedMaterial;
    }

    private static Vector3 RandomDirInCone(Vector3 axis, float spreadDegrees)
    {
        Quaternion lookAlongAxis = Quaternion.LookRotation(axis);
        float pitch = Random.Range(0f, spreadDegrees);
        float yaw = Random.Range(0f, 360f);
        Quaternion offset = Quaternion.Euler(pitch, 0f, 0f);
        Quaternion spin = Quaternion.AngleAxis(yaw, Vector3.forward);
        return lookAlongAxis * spin * offset * Vector3.forward;
    }
}

/// <summary>
/// Tiny lifecycle component attached to each spawned particle so the
/// HitParticles utility knows when the GameObject has been destroyed and
/// can free a slot from the active-count cap.
/// </summary>
public class HitParticleLifecycle : MonoBehaviour
{
    private void OnDestroy()
    {
        HitParticles.NotifyDestroyed();
    }
}
