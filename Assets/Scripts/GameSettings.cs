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

    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>
    /// Knobs the difficulty preset feeds into the various gameplay systems.
    /// Tunable from the GameSettings inspector — the defaults below are
    /// applied if no preset has been edited.
    /// </summary>
    [System.Serializable]
    public struct DifficultyPreset
    {
        [Tooltip("Multiplier added to the per-minute regular-enemy HP scaling. Higher = enemies tougher faster.")]
        public float regularHpBonusPerMinute;
        [Tooltip("Seconds between boss spawns. Lower = bosses arrive more often.")]
        public float bossSpawnInterval;
        [Tooltip("Boss HP exponent: each boss is this multiple of the previous one's HP.")]
        public float bossHpMultiplierPerSpawn;
        [Tooltip("SlimeKing's attack-speed boost multiplier in ranged mode. Lower = faster ranged attacks (so EASY = closer to 1, HARD = lower).")]
        public float slimeKingAttackSpeedBoostMultiplier;
        [Tooltip("Multiplier on every Crossbow-behavior enemy's projectile damage (incl. SlimeKing's ranged shots).")]
        public float crossbowDamageMultiplier;
        [Tooltip("Multiplier applied to the final boss (SlimeGod) — feeds into its attackDamageMultiplier so every damaging move it does (dash, beam, arrows, etc.) scales together.")]
        public float finalBossDamageMultiplier;
    }

    [Header("Difficulty Presets")]
    public DifficultyPreset easyPreset = new DifficultyPreset
    {
        regularHpBonusPerMinute = 0.50f,
        bossSpawnInterval = 60f,
        bossHpMultiplierPerSpawn = 1.5f,
        slimeKingAttackSpeedBoostMultiplier = 0.75f,
        crossbowDamageMultiplier = 0.6f,
        finalBossDamageMultiplier = 0.7f,
    };
    public DifficultyPreset normalPreset = new DifficultyPreset
    {
        regularHpBonusPerMinute = 1f,
        bossSpawnInterval = 60f,
        bossHpMultiplierPerSpawn = 2.5f,
        slimeKingAttackSpeedBoostMultiplier = 0.3f,
        crossbowDamageMultiplier = 1.0f,
        finalBossDamageMultiplier = 1.0f,
    };
    public DifficultyPreset hardPreset = new DifficultyPreset
    {
        regularHpBonusPerMinute = 3f,
        bossSpawnInterval = 60f,
        bossHpMultiplierPerSpawn = 5f,
        slimeKingAttackSpeedBoostMultiplier = 0.1f,
        crossbowDamageMultiplier = 1.5f,
        finalBossDamageMultiplier = 1.4f,
    };

    public DifficultyPreset GetActivePreset()
    {
        switch (CurrentDifficulty)
        {
            case Difficulty.Easy: return easyPreset;
            case Difficulty.Hard: return hardPreset;
            default:              return normalPreset;
        }
    }

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
    private const string PrefsMusicVol    = "Settings.MusicVolume";
    private const string PrefsDifficulty  = "Settings.Difficulty";

    /// <summary>Target framerate. 0 = uncapped.</summary>
    public int TargetFps { get; private set; } = 60;
    /// <summary>Target resolution in pixels.</summary>
    public Vector2Int Resolution { get; private set; } = new Vector2Int(1920, 1080);
    /// <summary>True for fullscreen, false for windowed.</summary>
    public bool Fullscreen { get; private set; } = true;

    /// <summary>Music volume from 0 (mute) to 1 (full). Applied via AudioListener.volume.</summary>
    public float MusicVolume { get; private set; } = 0.7f;

    /// <summary>Active difficulty. Changes only take effect on the next new game.</summary>
    public Difficulty CurrentDifficulty { get; private set; } = Difficulty.Normal;

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
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefsMusicVol, 0.7f));
        int diff = PlayerPrefs.GetInt(PrefsDifficulty, (int)Difficulty.Normal);
        CurrentDifficulty = (Difficulty)Mathf.Clamp(diff, (int)Difficulty.Easy, (int)Difficulty.Hard);
    }

    public void Save()
    {
        PlayerPrefs.SetInt(PrefsTargetFps,  TargetFps);
        PlayerPrefs.SetInt(PrefsResWidth,   Resolution.x);
        PlayerPrefs.SetInt(PrefsResHeight,  Resolution.y);
        PlayerPrefs.SetInt(PrefsFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.SetFloat(PrefsMusicVol, MusicVolume);
        PlayerPrefs.SetInt(PrefsDifficulty, (int)CurrentDifficulty);
        PlayerPrefs.Save();
    }

    public void Apply()
    {
        ApplyFps();
        ApplyDisplay();
        ApplyAudio();
    }

    /// <summary>
    /// Apply music volume — drives AudioListener.volume. If you later split
    /// audio into music/SFX channels via an AudioMixer, route the music
    /// channel's exposed parameter from here instead.
    /// </summary>
    public void ApplyAudio()
    {
        AudioListener.volume = Mathf.Clamp01(MusicVolume);
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
    public void SetMusicVolume(float v)      { MusicVolume = Mathf.Clamp01(v); ApplyAudio();  Save(); }
    /// <summary>Pick a difficulty preset. Doesn't apply to gameplay until the player starts a new run.</summary>
    public void SetDifficulty(Difficulty d)  { CurrentDifficulty = d; Save(); }

    /// <summary>Pretty label for the FPS option (e.g. "60 FPS" or "Unlimited").</summary>
    public static string FormatFps(int fps) => fps <= 0 ? "Unlimited" : fps + " FPS";

    /// <summary>Pretty label for a resolution (e.g. "1920×1080").</summary>
    public static string FormatResolution(Vector2Int r) => r.x + "×" + r.y;
}
