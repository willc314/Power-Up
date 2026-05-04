using System.Collections;
using UnityEngine;

/// <summary>
/// Procedural expanding-ring shockwave used when the final boss spawns and on
/// its spawn-attack resolve. Animates a ground-aligned LineRenderer ring growing
/// from radius 0 to <c>maxRadius</c> while fading out.
///
/// Use <see cref="Spawn(Vector3, float, Color, float)"/> to fire-and-forget.
/// The GameObject self-destructs when the animation finishes.
/// </summary>
public class BossShockwave : MonoBehaviour
{
    public float maxRadius = 30f;
    public float duration = 0.6f;
    public Color color = new Color(1f, 0.3f, 0.3f, 0.95f);
    public float lineWidth = 0.5f;
    public int segments = 64;

    public static BossShockwave Spawn(Vector3 center, float maxRadius = 30f, Color? color = null, float duration = 0.6f)
    {
        GameObject go = new GameObject("BossShockwave");
        go.transform.position = center + Vector3.up * 0.05f;
        BossShockwave sw = go.AddComponent<BossShockwave>();
        sw.maxRadius = maxRadius;
        sw.duration = duration;
        sw.color = color ?? new Color(1f, 0.3f, 0.3f, 0.95f);
        return sw;
    }

    private void Start()
    {
        StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        var lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = segments;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.material = GetMaterial();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float r = Mathf.Lerp(0.5f, maxRadius, k);
            float alpha = 1f - k;
            Color c = new Color(color.r, color.g, color.b, color.a * alpha);
            lr.startColor = c;
            lr.endColor = c;
            float w = Mathf.Lerp(lineWidth * 1.4f, lineWidth * 0.4f, k);
            lr.startWidth = w;
            lr.endWidth = w;

            for (int i = 0; i < segments; i++)
            {
                float a = (360f / segments) * i * Mathf.Deg2Rad;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    private static Material cachedMat;
    private static Material GetMaterial()
    {
        if (cachedMat != null) return cachedMat;
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        cachedMat = new Material(s);
        cachedMat.hideFlags = HideFlags.HideAndDontSave;
        return cachedMat;
    }
}
