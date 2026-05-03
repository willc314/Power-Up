using UnityEngine;

/// <summary>
/// Persistent player-facing settings: FPS cap, resolution, fullscreen.
/// Auto-initialized before the first scene loads (via
/// RuntimeInitializeOnLoadMethod), survives scene changes, and applies the
/// stored values to Application + Screen on startup.
///
/// Read/write via the static API:
///   GameSettings.Instance.SetTargetFps(60);
///   GameSettings.Instance.SetResolution(new Vector2Int(1920, 1080));
///   GameSettings.Instance.SetFullscreen(true);
///
/// Each setter persists to PlayerPrefs immediately and re-applies, so the
/// next launch picks up where the player left off.
/// </summary>
public class GameSettings : MonoBehaviour
{
    public static GameSettings Instance { get; private set; }

    /// <summary>Common FPS caps shown in the options menu. 0 means uncapped.</summary>
    public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144, 180, 240, 0 };

    /// <summary>Common resolutions shown in the options menu.</summary>
    public static readonly Vector2Int[] ResolutionOptions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3840, 2160),
    };

    private const string PrefsTargetFps   = "Settings.TargetFps";
    private const string PrefsResWidth    = "Settings.ResolutionWidth";
    private const string PrefsResHeight   = "Settings.ResolutionHeight";
    private const string PrefsFullscreen  = "Settings.Fullscreen";

    /// <summary>Target framerate. 0 = uncapped.</summary>
    public int TargetFps { get; private set; } = 60;
    /// <summary>Target resolution in pixels.</summary>
    public Vector2Int Resolution { get; private set; } = new Vector2Int(1920, 1080);
    /// <summary>True for fullscreen, false for windowed.</summary>
    public bool Fullscreen { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("GameSettings");
        Instance = go.AddComponent<GameSettings>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
        Apply();
    }

    public void Load()
    {
        TargetFps  = PlayerPrefs.GetInt(PrefsTargetFps, 60);
        int w      = PlayerPrefs.GetInt(PrefsResWidth,  Screen.currentResolution.width);
        int h      = PlayerPrefs.GetInt(PrefsResHeight, Screen.currentResolution.height);
        Resolution = new Vector2Int(Mathf.Max(640, w), Mathf.Max(360, h));
        Fullscreen = PlayerPrefs.GetInt(PrefsFullscreen, Screen.fullScreen ? 1 : 0) != 0;
    }

    public void Save()
    {
        PlayerPrefs.SetInt(PrefsTargetFps,  TargetFps);
        PlayerPrefs.SetInt(PrefsResWidth,   Resolution.x);
        PlayerPrefs.SetInt(PrefsResHeight,  Resolution.y);
        PlayerPrefs.SetInt(PrefsFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void Apply()
    {
        ApplyFps();
        ApplyDisplay();
    }

    /// <summary>Apply the FPS cap only — cheap, safe to call every frame.</summary>
    public void ApplyFps()
    {
        // Disabling vsync is required for Application.targetFrameRate to take
        // effect on most platforms. The framerate cap is what the player
        // actually controls in the options menu.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFps <= 0 ? -1 : TargetFps;
    }

    /// <summary>Apply resolution + fullscreen — relatively expensive, only call when those change.</summary>
    public void ApplyDisplay()
    {
        FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(Resolution.x, Resolution.y, mode);
    }

    public void SetTargetFps(int fps)        { TargetFps = Mathf.Max(0, fps); ApplyFps();     Save(); }
    public void SetResolution(Vector2Int r)  { Resolution = r;                ApplyDisplay(); Save(); }
    public void SetFullscreen(bool fs)       { Fullscreen = fs;               ApplyDisplay(); Save(); }

    /// <summary>Pretty label for the FPS option (e.g. "60 FPS" or "Unlimited").</summary>
    public static string FormatFps(int fps) => fps <= 0 ? "Unlimited" : fps + " FPS";

    /// <summary>Pretty label for a resolution (e.g. "1920×1080").</summary>
    public static string FormatResolution(Vector2Int r) => r.x + "×" + r.y;
}
