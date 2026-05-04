using UnityEngine;

/// <summary>
/// Ground-level "burning area" patch the SlimeGod drops along its dash path.
/// Renders a flat red disc, ticks DPS damage to <see cref="Hero.Instance"/>
/// while the hero is standing on it, and fades out over its lifetime before
/// destroying itself.
///
/// Spawn via <see cref="Spawn"/> from anywhere — fully self-contained, no
/// scene wiring required.
/// </summary>
public class BossBurnPatch : MonoBehaviour
{
    public float duration = 3f;
    public float radius = 1.5f;
    public float damagePerSecond = 25f;
    public Color color = new Color(1f, 0.18f, 0.12f, 0.8f);
    public float damageTickInterval = 0.1f;

    private float age;
    private float damageTimer;
    private MeshRenderer meshRenderer;
    private Material mat;

    /// <summary>
    /// Spawn a burn patch at <paramref name="pos"/> (slightly above ground so
    /// it doesn't z-fight with the floor). Fully detached from the boss —
    /// destroying the boss won't kill in-flight burn patches.
    /// </summary>
    public static BossBurnPatch Spawn(Vector3 pos, float radius, float duration,
                                      float damagePerSecond, Color color)
    {
        GameObject go = new GameObject("BossBurnPatch");
        go.transform.position = pos + Vector3.up * 0.05f;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        // Build a triangle-fan disc mesh of the requested radius. Lying flat
        // on the XZ plane.
        const int seg = 32;
        Vector3[] verts = new Vector3[seg + 1];
        int[] tris = new int[seg * 3];
        verts[0] = Vector3.zero;
        for (int i = 0; i < seg; i++)
        {
            float a = (360f / seg) * i * Mathf.Deg2Rad;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }
        for (int i = 0; i < seg; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % seg + 1;
        }
        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mf.mesh = mesh;

        // Material: same unlit transparent shader the telegraphs use, so the
        // patch reads as a glow rather than a textured surface.
        Material material = new Material(GetBurnShader());
        material.color = color;
        material.hideFlags = HideFlags.HideAndDontSave;
        mr.material = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var patch = go.AddComponent<BossBurnPatch>();
        patch.duration = duration;
        patch.radius = radius;
        patch.damagePerSecond = damagePerSecond;
        patch.color = color;
        patch.meshRenderer = mr;
        patch.mat = material;
        return patch;
    }

    private void Update()
    {
        age         += Time.deltaTime;
        damageTimer += Time.deltaTime;

        // Damage tick: hero on the disc takes DPS × tickDelta.
        if (damageTimer >= damageTickInterval && Hero.Instance != null && !Hero.Instance.IsDead)
        {
            Vector3 d = Hero.Instance.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= radius * radius)
                Hero.Instance.TakeDamage(damagePerSecond * damageTimer);
            damageTimer = 0f;
        }

        // Visual fade. Hold the full color for ~70% of the duration, then
        // ease alpha to 0 over the last 30%.
        if (mat != null)
        {
            float fadeStart = duration * 0.7f;
            float alpha = age < fadeStart
                ? color.a
                : Mathf.Lerp(color.a, 0f, (age - fadeStart) / Mathf.Max(0.0001f, duration - fadeStart));
            Color c = color; c.a = alpha;
            mat.color = c;
        }

        if (age >= duration) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }

    private static Shader GetBurnShader()
    {
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        return s;
    }
}
