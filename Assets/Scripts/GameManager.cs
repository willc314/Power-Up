using UnityEngine;

/// <summary>
/// Tracks per-run state: elapsed survival time, enemy kills, and the
/// derived score. Designed as a lightweight singleton (mirrors the
/// Hero.Instance pattern).
///
/// Score formula (chosen by the user):
///     Score = kills * pointsPerKill + floor(elapsedTime * pointsPerSecond)
///
/// Setup:
///   * Create an empty GameObject named "GameManager" in your scene.
///   * Add this component.
///   * (Optional) tweak pointsPerKill / pointsPerSecond in the Inspector.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Score")]
    [Tooltip("Points awarded for each enemy killed.")]
    public int pointsPerKill = 10;
    [Tooltip("Points the hero earns per second alive (survival bonus).")]
    public float pointsPerSecond = 1f;

    [Header("Behavior")]
    [Tooltip("If true, the timer pauses once the hero dies.")]
    public bool stopTimerOnDeath = true;

    [Header("Debug / Read-only")]
    [SerializeField] private float elapsedTime;
    [SerializeField] private int kills;

    /// <summary>Seconds the hero has been alive this run.</summary>
    public float ElapsedTime => elapsedTime;

    /// <summary>Total enemies killed this run.</summary>
    public int Kills => kills;

    /// <summary>Computed score: per-kill points + per-second survival bonus.</summary>
    public int Score => kills * pointsPerKill + Mathf.FloorToInt(elapsedTime * pointsPerSecond);

    /// <summary>True while the timer is advancing (game is in progress).</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>Global access. Set in Awake, cleared in OnDestroy.</summary>
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Another GameManager already exists; remove this duplicate.
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsRunning) return;

        // Stop counting if the hero has died.
        if (stopTimerOnDeath && Hero.Instance != null && Hero.Instance.IsDead)
        {
            IsRunning = false;
            return;
        }

        elapsedTime += Time.deltaTime;
    }

    /// <summary>Called by Enemy.Die() so the manager can update the score.</summary>
    public void OnEnemyKilled(Enemy enemy)
    {
        if (!IsRunning) return;
        kills++;
    }

    /// <summary>Resets the run. Call from a "restart" button.</summary>
    public void ResetRun()
    {
        elapsedTime = 0f;
        kills = 0;
        IsRunning = true;
    }

    /// <summary>
    /// Helper for HUD: format the elapsed time as MM:SS.
    /// </summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int total = Mathf.FloorToInt(seconds);
        int m = total / 60;
        int s = total % 60;
        return string.Format("{0:00}:{1:00}", m, s);
    }
}
