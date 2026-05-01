using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Briefly tints all of an entity's materials toward a flash color when it
/// takes damage, then fades back. Drop on the Hero, on each Enemy prefab, or
/// any GameObject with renderers. Hero and Enemy scripts call Flash()
/// automatically if they find this component.
///
/// How it works:
///   - On Awake, gathers every Renderer in children and instances their
///     materials so tinting one entity doesn't tint others sharing the same
///     base material.
///   - Supports both built-in/Standard (_Color) and URP/HDRP (_BaseColor) shaders.
///   - Flash() snaps the material color to flashColor, then lerps it back to
///     the original over flashDuration. Re-calling Flash() restarts the lerp.
/// </summary>
public class DamageFlash : MonoBehaviour
{
    [Header("Flash")]
    [Tooltip("Color the model snaps to when Flash() is called.")]
    public Color flashColor = Color.red;
    [Tooltip("How long it takes to fade from the flash color back to the original.")]
    public float flashDuration = 0.12f;

    [Header("Targets")]
    [Tooltip("Optional. If empty, all Renderers in children are used (typical case).")]
    public Renderer[] renderers;

    private static readonly int kBaseColor = Shader.PropertyToID("_BaseColor"); // URP / HDRP / Lit
    private static readonly int kColor     = Shader.PropertyToID("_Color");     // built-in / Standard

    // One entry per (renderer, material slot) — caches the original color and which property to set.
    private struct MatRef { public Material mat; public int prop; public Color original; }
    private readonly List<MatRef> entries = new List<MatRef>();
    private Coroutine flashRoutine;

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // .materials (plural) instances per-renderer materials so we can tint without affecting others.
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;

                int prop;
                if      (m.HasProperty(kBaseColor)) prop = kBaseColor;
                else if (m.HasProperty(kColor))     prop = kColor;
                else continue; // shader has no color property to tint

                entries.Add(new MatRef { mat = m, prop = prop, original = m.GetColor(prop) });
            }
        }
    }

    /// <summary>Trigger a damage flash. Safe to call repeatedly — restarts the fade.</summary>
    public void Flash()
    {
        if (!isActiveAndEnabled || entries.Count == 0) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        // Snap to flash color.
        for (int i = 0; i < entries.Count; i++)
            entries[i].mat.SetColor(entries[i].prop, flashColor);

        // Lerp back to the originals.
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / flashDuration);
            for (int i = 0; i < entries.Count; i++)
            {
                Color c = Color.Lerp(flashColor, entries[i].original, a);
                entries[i].mat.SetColor(entries[i].prop, c);
            }
            yield return null;
        }

        // Final snap to exact originals (avoid tiny floating-point residue).
        RestoreOriginals();
        flashRoutine = null;
    }

    private void RestoreOriginals()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].mat != null)
                entries[i].mat.SetColor(entries[i].prop, entries[i].original);
        }
    }

    private void OnDisable()
    {
        // Don't leave the model frozen mid-flash if disabled or destroyed.
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        RestoreOriginals();
    }
}
