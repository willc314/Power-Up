using UnityEngine;

/// <summary>
/// Game-wide one-shot sound-effect player. Singleton, auto-spawned at game
/// start (no scene asset needed), and persists across scene transitions.
/// Owns a small pool of AudioSources so overlapping SFX don't cut each other
/// off the way <see cref="AudioSource.PlayOneShot"/> on a single source can.
///
/// Usage from any other script:
///   SoundManager.Instance.PlaySfxAt(clip, transform.position);
///   SoundManager.Instance.PlaySfx(uiClick);          // 2D, no spatialization
///   SoundManager.Instance.PlaySfxAt(impactClip, pos, volume: 0.8f, pitch: 1.05f);
///
/// MusicManager handles looping music with crossfades — this class is
/// strictly for short, one-shot effects (impacts, swings, UI clicks, etc).
/// They don't need to crossfade and they DO need to overlap, so the two
/// systems are kept separate.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Mixing")]
    [Tooltip("Master multiplier on every SFX played through this manager. 1 = full, 0 = mute. Hook this up to a settings slider if you want a separate SFX volume control.")]
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Number of pooled AudioSources used to play overlapping SFX. If you ever see SFX cutting each other off in busy moments, raise this.")]
    public int poolSize = 12;

    [Header("3D Defaults")]
    [Tooltip("Spatial blend used by PlaySfxAt. 0 = pure 2D (volume identical regardless of distance), 1 = pure 3D (volume falls off with distance from listener). 0.7 keeps the SFX audible even when the camera is offset but still gives a sense of position.")]
    [Range(0f, 1f)] public float spatialBlend3D = 0.7f;
    [Tooltip("Min distance (units) for 3D SFX. Inside this radius volume is at max.")]
    public float minDistance3D = 5f;
    [Tooltip("Max distance (units) for 3D SFX. Outside this radius volume is at floor.")]
    public float maxDistance3D = 60f;

    private AudioSource[] pool;
    private int nextPoolIndex;

    /// <summary>
    /// Auto-spawn the singleton before any gameplay scene loads. Mirrors the
    /// pattern other managers (LevelUpChoiceUI, GameSettings) use so callers
    /// can rely on Instance existing without anyone having to drop a prefab
    /// in the scene first. Persists across scene loads so a single instance
    /// survives the menu → gameplay transition.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("SoundManager");
        go.AddComponent<SoundManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Pull persisted SFX volume from GameSettings so the player's
        // saved slider value takes effect immediately without anyone
        // having to call ApplyAudio after the singleton spawns.
        if (GameSettings.Instance != null)
            volume = Mathf.Clamp01(GameSettings.Instance.SfxVolume);

        // Pre-allocate the AudioSource pool. Each source is a child of the
        // manager so its lifetime tracks ours, and each is configured up
        // front with the default 3D rolloff so PlaySfxAt doesn't have to
        // reach into AudioSource internals on the hot path.
        pool = new AudioSource[Mathf.Max(1, poolSize)];
        for (int i = 0; i < pool.Length; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = spatialBlend3D;
            src.minDistance = minDistance3D;
            src.maxDistance = maxDistance3D;
            src.rolloffMode = AudioRolloffMode.Linear;
            pool[i] = src;
        }
    }

    /// <summary>
    /// Play a one-shot 2D SFX (no spatialization). Use for UI clicks,
    /// hero-side events that should always sound at full volume regardless
    /// of camera position.
    /// </summary>
    public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        var src = NextSource();
        src.spatialBlend = 0f; // 2D
        src.transform.localPosition = Vector3.zero;
        src.pitch = Mathf.Max(0.01f, pitch);
        src.PlayOneShot(clip, Mathf.Clamp01(volume) * Mathf.Clamp01(this.volume));
    }

    /// <summary>
    /// Play a one-shot SFX at a world position with the manager's default
    /// 3D rolloff. Use for in-world events — impacts, projectile launches,
    /// enemy attacks. The source is moved to <paramref name="position"/>
    /// before play so distance attenuation works correctly.
    /// </summary>
    public void PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        var src = NextSource();
        src.spatialBlend = spatialBlend3D;
        src.minDistance = minDistance3D;
        src.maxDistance = maxDistance3D;
        src.transform.position = position;
        src.pitch = Mathf.Max(0.01f, pitch);
        src.PlayOneShot(clip, Mathf.Clamp01(volume) * Mathf.Clamp01(this.volume));
    }

    /// <summary>
    /// Round-robin allocator over the pool. Returns whichever source is
    /// next in line — even if it's currently playing. PlayOneShot stacks
    /// multiple plays on the same source, so reusing a busy slot is fine
    /// for SFX (it sounds like a quick echo / overlap, not a cut-off).
    /// </summary>
    private AudioSource NextSource()
    {
        var src = pool[nextPoolIndex];
        nextPoolIndex = (nextPoolIndex + 1) % pool.Length;
        return src;
    }
}
