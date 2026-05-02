using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor utility that renders one or more 3D prefabs into transparent PNG
/// sprite icons. Useful for turning weapon models (Sword.prefab, Bow.prefab,
/// etc.) into icons the HUD can show.
///
/// Usage:
///   1. In the Project window, select one or more prefabs (e.g. weapon visual
///      prefabs from Assets/Imported Packs/Low-Poly Weapons/).
///   2. Menu:  Tools / Power-Up / Render Icons For Selected Prefabs
///   3. A small window opens. Set the per-prefab rotation (Euler) you want
///      applied before rendering, then click Render All.
///   4. PNGs are written to Assets/Sprites/Weapons/{PrefabName}.png and
///      imported automatically as Sprites (alpha-as-transparency).
///   5. Drag the resulting sprite into each Weapon component's "Hud Icon" field.
///
/// Knobs (constants below): icon resolution, camera angle, output folder,
/// padding around the model, light direction.
/// </summary>
public static class WeaponIconRenderer
{
    // -------------------- Tunables --------------------
    public const int    IconSize      = 256;
    public const string OutputFolder  = "Assets/Sprites/Weapons";
    // 3/4 isometric: tilt down 30°, yaw 45° around the model.
    public static readonly Vector3 CameraEuler = new Vector3(30f, -45f, 0f);
    // What fraction of the icon's frame the model fills. 0.85 leaves a
    // ~15% margin around the model on each side.
    public const float  FillRatio     = 0.85f;
    // Dedicated layer used so the icon camera and light don't see anything
    // else in the scene. Layer 31 is normally unused.
    public const int    PreviewLayer  = 31;
    public static readonly Vector3 LightEuler  = new Vector3(40f, -30f, 0f);
    public const float  LightIntensity = 1.4f;
    // Subtle ambient so the back/underside of the model isn't pure black.
    public static readonly Color AmbientFill   = new Color(0.35f, 0.35f, 0.4f);

    // -------------------- Menu --------------------

    [MenuItem("Tools/Power-Up/Render Icons For Selected Prefabs")]
    public static void RenderSelected()
    {
        var prefabs = new List<GameObject>();
        foreach (var obj in Selection.objects)
        {
            if (obj is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go))
                prefabs.Add(go);
        }
        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Render Weapon Icons",
                "Select one or more prefabs in the Project window first " +
                "(e.g. Sword.prefab, Bow.prefab, Shield.prefab).",
                "OK");
            return;
        }

        // Open the per-prefab rotation prompt instead of rendering directly.
        WeaponIconRendererWindow.Open(prefabs);
    }

    // -------------------- Public render entry point --------------------

    /// <summary>
    /// Renders each prefab to a PNG. <paramref name="rotations"/> must be
    /// the same length as <paramref name="prefabs"/>; each entry is the Euler
    /// rotation (degrees) applied to the prefab's transform before bounds are
    /// computed and the camera is framed.
    /// </summary>
    public static void RenderPrefabs(List<GameObject> prefabs, List<Vector3> rotations)
    {
        if (prefabs == null || prefabs.Count == 0) return;
        EnsureFolder(OutputFolder);

        // --- Build the throw-away rig: camera + directional light + render texture.
        GameObject camGo = new GameObject("IconCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic    = true;
        cam.nearClipPlane   = 0.01f;
        cam.farClipPlane    = 100f;
        cam.cullingMask     = 1 << PreviewLayer;
        cam.allowHDR        = false;
        cam.allowMSAA       = true;
        camGo.transform.rotation = Quaternion.Euler(CameraEuler);

        GameObject lightGo = new GameObject("IconLight") { hideFlags = HideFlags.HideAndDontSave };
        Light light = lightGo.AddComponent<Light>();
        light.type        = LightType.Directional;
        light.color       = Color.white;
        light.intensity   = LightIntensity;
        light.cullingMask = 1 << PreviewLayer;
        lightGo.transform.rotation = Quaternion.Euler(LightEuler);

        // Snapshot then override ambient so the icon doesn't depend on the
        // scene's lighting environment.
        Color    prevAmbientLight = RenderSettings.ambientLight;
        AmbientMode prevAmbientMode = RenderSettings.ambientMode;
        RenderSettings.ambientMode  = AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientFill;

        RenderTexture rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;

        try
        {
            int rendered = 0;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                Vector3 rotation = (rotations != null && i < rotations.Count) ? rotations[i] : Vector3.zero;
                EditorUtility.DisplayProgressBar(
                    "Rendering weapon icons",
                    prefab.name,
                    (i + 1f) / prefabs.Count);
                if (RenderOne(prefab, rotation, cam, rt)) rendered++;
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Render Weapon Icons",
                $"Rendered {rendered} icon(s) to {OutputFolder}",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            RenderSettings.ambientLight = prevAmbientLight;
            RenderSettings.ambientMode  = prevAmbientMode;
        }
    }

    private static bool RenderOne(GameObject prefab, Vector3 rotationOverride, Camera cam, RenderTexture rt)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null) return false;
        instance.hideFlags = HideFlags.HideAndDontSave;

        // Move it onto the dedicated preview layer and disable physics so
        // nothing the prefab carries (Rigidbody, Collider) interferes.
        SetLayerRecursively(instance, PreviewLayer);
        foreach (var c in instance.GetComponentsInChildren<Collider>(true))   c.enabled = false;
        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        // Disable all MonoBehaviours on the instance — we just want its mesh/
        // materials, not whatever Update/Awake logic the prefab might run.
        foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

        // Apply the user-requested rotation BEFORE measuring bounds and
        // framing the camera. This is additive on top of the prefab's
        // authored rotation, so a value of (0,0,0) means "as authored".
        instance.transform.rotation = Quaternion.Euler(rotationOverride) * instance.transform.rotation;

        // Combined renderer bounds in world space (post-rotation).
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[WeaponIconRenderer] '{prefab.name}' has no renderers; skipped.");
            Object.DestroyImmediate(instance);
            return false;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        // Re-center the model at world origin so framing is symmetrical.
        instance.transform.position -= bounds.center;
        bounds.center = Vector3.zero;

        // Project the bounding-box corners into camera space so we can size
        // the orthographic frustum to fit the model regardless of the
        // weapon's natural orientation. orthographicSize is HALF the camera's
        // view height in world units; we want the largest projected half-
        // extent of the model to occupy FillRatio of the frame.
        float halfExtent = ProjectMaxExtent(bounds, cam.transform.rotation);
        cam.orthographicSize = Mathf.Max(0.05f, halfExtent / Mathf.Max(0.01f, FillRatio));
        // Pull the camera back along its forward axis so the model is in front of it.
        cam.transform.position = -cam.transform.forward * 10f;

        // Render into the RenderTexture and copy to a CPU-side Texture2D.
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        cam.Render();

        Texture2D tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(instance);

        string path = $"{OutputFolder}/{SanitizeFileName(prefab.name)}.png";
        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);
        ConfigureSpriteImport(path);
        return true;
    }

    /// <summary>
    /// Returns the largest absolute X/Y of the bounds corners after rotating
    /// them into the camera's local space. Used to size the orthographic
    /// camera so the model fits regardless of weapon shape.
    /// </summary>
    private static float ProjectMaxExtent(Bounds bounds, Quaternion camRot)
    {
        Vector3 ext = bounds.extents;
        Quaternion inv = Quaternion.Inverse(camRot);
        float maxXY = 0f;
        for (int xs = -1; xs <= 1; xs += 2)
        for (int ys = -1; ys <= 1; ys += 2)
        for (int zs = -1; zs <= 1; zs += 2)
        {
            Vector3 corner = new Vector3(ext.x * xs, ext.y * ys, ext.z * zs);
            Vector3 v = inv * corner;
            maxXY = Mathf.Max(maxXY, Mathf.Abs(v.x), Mathf.Abs(v.y));
        }
        return maxXY;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursively(t.gameObject, layer);
    }

    private static void ConfigureSpriteImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType        = TextureImporterType.Sprite;
        importer.spriteImportMode   = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled      = false;
        importer.filterMode         = FilterMode.Bilinear;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string accumulated = parts[0]; // Always "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{accumulated}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(accumulated, parts[i]);
            accumulated = next;
        }
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid) name = name.Replace(c, '_');
        return name;
    }
}

/// <summary>
/// Modal-style EditorWindow that lists each selected prefab with a per-prefab
/// rotation (Euler degrees) applied to the model before rendering. Hit
/// "Render All" to write the PNGs.
/// </summary>
public class WeaponIconRendererWindow : EditorWindow
{
    private List<GameObject> prefabs = new List<GameObject>();
    private List<Vector3> rotations = new List<Vector3>();
    private Vector3 applyToAllRotation;
    private Vector2 scroll;

    private const string PrefsRotPrefix = "WeaponIconRenderer.Rotation.";

    public static void Open(List<GameObject> selected)
    {
        var w = GetWindow<WeaponIconRendererWindow>(true, "Render Weapon Icons", true);
        w.prefabs = new List<GameObject>(selected);
        w.rotations = new List<Vector3>(selected.Count);
        for (int i = 0; i < selected.Count; i++)
        {
            // Restore last-used rotation per prefab if we saved one.
            string key = PrefsRotPrefix + (selected[i] != null ? selected[i].name : "_");
            string saved = EditorPrefs.GetString(key, "");
            w.rotations.Add(ParseVec3(saved));
        }
        w.minSize = new Vector2(420, 220);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Set the rotation (degrees) applied to each prefab before rendering. " +
            "Common quick fixes: Y = 90 / -90 to spin sideways weapons; X = -90 to lay a weapon flat. " +
            "Defaults are remembered per prefab.",
            MessageType.Info);

        // Apply-to-all helper
        EditorGUILayout.BeginHorizontal();
        applyToAllRotation = EditorGUILayout.Vector3Field("Apply to all:", applyToAllRotation);
        if (GUILayout.Button("Set", GUILayout.Width(48)))
        {
            for (int i = 0; i < rotations.Count; i++) rotations[i] = applyToAllRotation;
        }
        if (GUILayout.Button("Reset", GUILayout.Width(56)))
        {
            applyToAllRotation = Vector3.zero;
            for (int i = 0; i < rotations.Count; i++) rotations[i] = Vector3.zero;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Per-prefab list
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < prefabs.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            string label = prefabs[i] != null ? prefabs[i].name : "(missing)";
            EditorGUILayout.LabelField(label, GUILayout.Width(160));
            rotations[i] = EditorGUILayout.Vector3Field(GUIContent.none, rotations[i]);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel"))
        {
            Close();
            return;
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Render All", GUILayout.Width(140)))
        {
            // Save chosen rotations so they're remembered next time.
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null) continue;
                EditorPrefs.SetString(PrefsRotPrefix + prefabs[i].name, FormatVec3(rotations[i]));
            }

            WeaponIconRenderer.RenderPrefabs(prefabs, rotations);
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }

    private static string FormatVec3(Vector3 v)
    {
        return $"{v.x},{v.y},{v.z}";
    }

    private static Vector3 ParseVec3(string s)
    {
        if (string.IsNullOrEmpty(s)) return Vector3.zero;
        string[] p = s.Split(',');
        if (p.Length != 3) return Vector3.zero;
        float x, y, z;
        if (!float.TryParse(p[0], out x)) x = 0f;
        if (!float.TryParse(p[1], out y)) y = 0f;
        if (!float.TryParse(p[2], out z)) z = 0f;
        return new Vector3(x, y, z);
    }
}
