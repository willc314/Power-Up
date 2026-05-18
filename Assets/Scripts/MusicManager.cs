using System.Collections;
using UnityEngine;

/// <summary>
/// Scene music manager with crossfading between three named tracks: background,
/// boss, victory. Drop one in your scene with the clips assigned.
///
/// Usage from other scripts:
///   MusicManager.Instance.PlayBackground();   // normal arena music
///   MusicManager.Instance.PlayBoss();         // call when the final boss spawns
///   MusicManager.Instance.PlayVictory();      // call when the victory screen opens
///   MusicManager.Instance.PlayBackground();   // call again from the "Continue" button to restore arena music
///   MusicManager.Instance.Stop();             // fade everything out
///
/// Implementation detail: two AudioSources cross-fade between each other,
/// driven by a coroutine on Time.unscaledDeltaTime (so the fade still runs
/// when Time.timeScale = 0 during a paused victory screen).
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Tracks")]
    [Tooltip("Looping music played for the normal arena (or whenever no other track is requested).")]
    public AudioClip backgroundClip;
    [Tooltip("Looping music played when the final boss is active. Triggered manually via PlayBoss().")]
    public AudioClip bossClip;
    [Tooltip("Music played when the victory screen opens. Looping is fine but optional — set Loop Victory to false if it's a one-shot fanfare.")]
    public AudioClip victoryClip;
    [Tooltip("Looping music for the title screen. Played by MainMenu.Start (or call PlayTitle() yourself).")]
    public AudioClip titleClip;
    [Tooltip("Music for the end / game over screen. Looping is optional — set Loop End to false for a one-shot sting.")]
    public AudioClip endClip;

    [Header("Mixing")]
    [Tooltip("Master music volume.")]
    [Range(0f, 1f)] public float volume = 0.7f;
    [Tooltip("Seconds for crossfading between tracks. Lower = snappier transitions, higher = smoother.")]
    public float crossfadeDuration = 1.5f;

    [Header("Ducking (menus)")]
    [Tooltip("Multiplier applied to volume while ducked (e.g. while a menu is open). 0.3 = drop to 30% of normal.")]
    [Range(0f, 1f)] public float duckLevel = 0.3f;
    [Tooltip("Seconds to fade between normal and ducked volume.")]
    public float duckFadeTime = 0.25f;
    [Tooltip("If true, the victory clip loops while the victory screen is open. If false, it plays once then leaves silence (until PlayBackground is called).")]
    public bool loopVictory = true;
    [Tooltip("If true, the end clip loops on the game-over screen. Set false for a one-shot sting that fades to silence.")]
    public bool loopEnd = true;
    [Tooltip("If true, automatically plays the background clip on Start. Turn off if your scene loader needs to start music manually.")]
    public bool playOnStart = true;
    [Tooltip("If true, the manager survives scene changes. Useful if the victory screen is a separate scene; leave off if every scene has its own MusicManager.")]
    public bool persistAcrossScenes = false;

    private AudioSource sourceA;
    private AudioSource sourceB;
    private AudioSource activeSource;
    private Coroutine fadeRoutine;
    private AudioClip currentClip;

    // Ducking state. Ref-counted so multiple overlapping menus (powerup +
    // settings) stack correctly: each opener calls BeginDuck, each closer
    // calls EndDuck, and the music only un-ducks when the count returns to 0.
    private int duckCount;
    private float currentDuckMultiplier = 1f;
    private float targetDuckMultiplier = 1f;

    public AudioClip CurrentClip => currentClip;
    public bool IsDucked => duckCount > 0;

    private void Awake()
    {
        // Singleton guard — destroy duplicates (the original keeps playing).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);

        // Pull persisted music volume from GameSettings so the player's
        // saved slider value takes effect even though GameSettings.Apply()
        // runs before any per-scene MusicManager exists.
        if (GameSettings.Instance != null)
            volume = Mathf.Clamp01(GameSettings.Instance.MusicVolume);

        sourceA = gameObject.AddComponent<AudioSource>();
        sourceB = gameObject.AddComponent<AudioSource>();
        ConfigureSource(sourceA);
        ConfigureSource(sourceB);
        activeSource = sourceA;
    }

    private void Start()
    {
        if (!playOnStart) return;
        // Auto-pick the most appropriate clip for the scene the manager is in.
        // Title scene: titleClip set, others empty → plays title.
        // Gameplay scene: backgroundClip set → plays background.
        // End scene: endClip set, background empty → plays end.
        if      (titleClip      != null && backgroundClip == null) PlayTitle();
        else if (endClip        != null && backgroundClip == null) PlayEnd();
        else if (backgroundClip != null) PlayBackground();
        else if (titleClip      != null) PlayTitle();
        else if (endClip        != null) PlayEnd();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static void ConfigureSource(AudioSource s)
    {
        s.playOnAwake = false;
        s.loop = true;
        s.volume = 0f;
        s.spatialBlend = 0f;     // 2D
        s.bypassEffects = false;
        s.bypassListenerEffects = false;
        s.bypassReverbZones = false;
    }

    // ---- Public API ----

    public void PlayBackground() => RequestPlay(backgroundClip, loop: true);
    public void PlayBoss()       => RequestPlay(bossClip,       loop: true);
    public void PlayVictory()    => RequestPlay(victoryClip,    loop: loopVictory);
    public void PlayTitle()      => RequestPlay(titleClip,      loop: true);
    public void PlayEnd()        => RequestPlay(endClip,        loop: loopEnd);

    /// <summary>Fade out everything, leaving silence.</summary>
    public void Stop()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(CrossfadeRoutine(null, loop: false));
        currentClip = null;
    }

    /// <summary>Set master volume. Updates the currently-playing source live.</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (activeSource != null && activeSource.isPlaying)
            activeSource.volume = volume * currentDuckMultiplier;
    }

    // ---- Ducking ----

    /// <summary>
    /// Drop the music to duckLevel (e.g. while a menu is open). Ref-counted, so
    /// nested menus stack correctly — each call must be paired with EndDuck().
    /// </summary>
    public void BeginDuck()
    {
        duckCount++;
        targetDuckMultiplier = duckLevel;
    }

    /// <summary>Pop one duck request. When the count returns to 0, music ramps back to full volume.</summary>
    public void EndDuck()
    {
        duckCount = Mathf.Max(0, duckCount - 1);
        targetDuckMultiplier = duckCount > 0 ? duckLevel : 1f;
    }

    /// <summary>Force-clear all duck requests (e.g. when reloading a scene).</summary>
    public void ResetDuck()
    {
        duckCount = 0;
        targetDuckMultiplier = 1f;
    }

    private void Update()
    {
        // Smoothly drift the duck multiplier toward its target. Uses
        // unscaledDeltaTime so the duck still fades while paused (Time.timeScale=0).
        if (currentDuckMultiplier != targetDuckMultiplier)
        {
            float step = duckFadeTime > 0f
                ? Time.unscaledDeltaTime / duckFadeTime
                : 1f;
            currentDuckMultiplier = Mathf.MoveTowards(currentDuckMultiplier, targetDuckMultiplier, step);

            // Apply on top of the active source's base volume — but only when
            // we're NOT mid-crossfade. The crossfade routine reads the duck
            // multiplier itself so it stays correct during the blend.
            if (fadeRoutine == null && activeSource != null && activeSource.isPlaying)
                activeSource.volume = volume * currentDuckMultiplier;
        }
    }

    // ---- Internals ----

    private void RequestPlay(AudioClip clip, bool loop)
    {
        if (clip == null) return;
        // Already playing this clip — nothing to do.
        if (currentClip == clip && activeSource != null && activeSource.isPlaying) return;
        currentClip = clip;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(CrossfadeRoutine(clip, loop));
    }

    private IEnumerator CrossfadeRoutine(AudioClip clip, bool loop)
    {
        AudioSource newSource = (activeSource == sourceA) ? sourceB : sourceA;
        AudioSource oldSource = activeSource;

        if (clip != null)
        {
            newSource.clip = clip;
            newSource.loop = loop;
            newSource.volume = 0f;
            newSource.Play();
        }

        float startOldVol = oldSource != null ? oldSource.volume : 0f;
        float t = 0f;
        // Use unscaledDeltaTime so the fade still runs if the game is paused
        // (e.g. victory screen often sets Time.timeScale = 0).
        while (t < crossfadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / crossfadeDuration);
            // Re-read duck multiplier each frame so a menu opening mid-crossfade
            // still ducks the incoming track correctly.
            float targetVol = volume * currentDuckMultiplier;
            if (clip != null) newSource.volume = targetVol * u;
            if (oldSource != null) oldSource.volume = startOldVol * (1f - u);
            yield return null;
        }

        if (oldSource != null)
        {
            oldSource.volume = 0f;
            oldSource.Stop();
        }
        if (clip != null)
        {
            newSource.volume = volume * currentDuckMultiplier;
            activeSource = newSource;
        }

        fadeRoutine = null;
    }
}
