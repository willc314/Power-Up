using UnityEngine;

/// <summary>
/// Tracks per-run state: elapsed survival time, enemy kills, derived score,
/// and the persistent high score across runs.
///
/// Score formula:
///     Score = kills × pointsPerKill
///           + bossKills × pointsPerBossKill
///           + floor(elapsedTime × pointsPerSecond)
///
/// High score is loaded from PlayerPrefs on Awake and saved when the hero
/// dies (along with the final score, so the EndScreen can read both).
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Score")]
    [Tooltip("Points awarded for each regular enemy kill.")]
    public int pointsPerKill = 10;
    [Tooltip("Points awarded for each boss kill (Enemy.Behavior.SlimeKing).")]
    public int pointsPerBossKill = 50;
    [Tooltip("Points the hero earns per second alive (survival bonus). The timer-based contribution stops accumulating while the final boss is alive.")]
    public float pointsPerSecond = 1f;
    [Tooltip("Lump-sum points awarded when the player kills the SlimeGod final boss.")]
    public int pointsForFinalBossKill = 1000;

    [Header("Behavior")]
    [Tooltip("If true, the timer pauses once the hero dies.")]
    public bool stopTimerOnDeath = true;

    [Header("Debug / Read-only")]
    [SerializeField] private float elapsedTime;
    [SerializeField] private int kills;
    [SerializeField] private int bossKills;

    // PlayerPrefs keys.
    public  const string PrefsHighScore     = "GameManager.HighScore";
    public  const string PrefsLastScore     = "GameManager.LastFinalScore";
    public  const string PrefsWasNewHigh    = "GameManager.LastRunWasNewHigh";
    public  const string PrefsLastWasWin    = "GameManager.LastRunWasWin";

    // Final-boss state.
    private SlimeGod activeFinalBoss;
    private bool finalBossKilled;
    private float survivalTimeFrozen;       // elapsedTime when the boss spawned
    private bool  survivalTimeFrozenSet;
    private int   bonusPoints;              // extra points (e.g. 1000 from boss kill)

    /// <summary>True while a SlimeGod final boss is alive in the scene.</summary>
    public bool IsFinalBossAlive => activeFinalBoss != null && !finalBossKilled;
    /// <summary>True after the player has killed the SlimeGod (used by the EndScreen for the Win readout).</summary>
    public bool VictoryThisRun => finalBossKilled;

    /// <summary>Seconds the hero has been alive this run.</summary>
    public float ElapsedTime => elapsedTime;

    /// <summary>Total enemies killed this run (regular + bosses).</summary>
    public int Kills => kills + bossKills;

    /// <summary>Total bosses killed this run.</summary>
    public int BossKills => bossKills;

    /// <summary>
    /// Computed score with all bonuses. Survival points stop accumulating
    /// while the final boss is alive (frozen at the elapsedTime when the boss
    /// spawned), and the boss kill itself adds <see cref="pointsForFinalBossKill"/>
    /// as a bonus.
    /// </summary>
    public int Score
        => kills           * pointsPerKill
         + bossKills       * pointsPerBossKill
         + Mathf.FloorToInt(EffectiveSurvivalTime * pointsPerSecond)
         + bonusPoints;

    /// <summary>
    /// Survival time used for scoring. Snapshots the moment the final boss
    /// spawned and only resumes after the boss is killed.
    /// </summary>
    public float EffectiveSurvivalTime
    {
        get
        {
            if (IsFinalBossAlive) return survivalTimeFrozen;
            return elapsedTime;
        }
    }

    /// <summary>True while the timer is advancing (game is in progress).</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>The high score loaded at the start of this run. Updated to current Score once the player surpasses it.</summary>
    public int HighScore { get; private set; }

    /// <summary>
    /// True from the moment the live Score first crosses the high score
    /// loaded at the start of the run. Stays true for the rest of the run.
    /// Always false on a brand-new save (HighScore == 0) so the banner
    /// doesn't fire on the very first point earned.
    /// </summary>
    public bool NewHighScoreThisRun { get; private set; }

    public static GameManager Instance { get; private set; }

    // Snapshot of HighScore at run start so NewHighScoreThisRun reads
    // correctly even if we update HighScore live while playing.
    private int startOfRunHighScore;
    private bool savedScores;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        HighScore = PlayerPrefs.GetInt(PrefsHighScore, 0);
        startOfRunHighScore = HighScore;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsRunning) return;

        if (stopTimerOnDeath && Hero.Instance != null && Hero.Instance.IsDead)
        {
            IsRunning = false;
            SaveScores();
            return;
        }

        elapsedTime += Time.deltaTime;

        // First-time-this-run high-score crossing.
        if (!NewHighScoreThisRun && startOfRunHighScore > 0 && Score > startOfRunHighScore)
            NewHighScoreThisRun = true;

        // Keep the displayed high score honest while the player is breaking it.
        if (Score > HighScore) HighScore = Score;
    }

    /// <summary>Called by Enemy.Die() so the manager can update kill counters.</summary>
    public void OnEnemyKilled(Enemy enemy)
    {
        if (!IsRunning) return;
        if (enemy == null) { kills++; return; }
        // SlimeGod is credited via OnFinalBossKilled, not the regular kill counter.
        if (enemy.behavior == Enemy.Behavior.SlimeGod) return;
        if (enemy.behavior == Enemy.Behavior.SlimeKing) bossKills++;
        else                                            kills++;
    }

    /// <summary>
    /// Called by SlimeGod when it spawns. Freezes the timer-based score
    /// contribution and notes which boss is active.
    /// </summary>
    public void NotifyFinalBossSpawned(SlimeGod boss)
    {
        activeFinalBoss = boss;
        if (!survivalTimeFrozenSet)
        {
            survivalTimeFrozen = elapsedTime;
            survivalTimeFrozenSet = true;
        }
    }

    /// <summary>Cleanup hook if the boss is destroyed without OnFinalBossKilled (e.g. scene unload).</summary>
    public void NotifyFinalBossDespawned()
    {
        activeFinalBoss = null;
    }

    /// <summary>
    /// Called by SlimeGod.OnBossKilled when the player kills the final boss.
    /// Awards the lump-sum bonus and triggers the in-game Win Menu (which
    /// pauses time and lets the player pick Continue / Restart / Title).
    /// </summary>
    public void OnFinalBossKilled()
    {
        if (finalBossKilled) return;
        finalBossKilled = true;
        bonusPoints += pointsForFinalBossKill;
        activeFinalBoss = null;

        // Wave the win flag through PlayerPrefs so a fallback EndScreen
        // load (no WinMenu in the scene) reads correctly.
        PlayerPrefs.SetInt(PrefsLastWasWin, 1);

        if (Hero.Instance != null) Hero.Instance.TriggerVictory();
    }

    /// <summary>
    /// Called by the Win Menu's Continue button. Clears the per-cycle "boss
    /// killed" flag and unfreezes survival-time scoring so the run keeps
    /// going. Bonus points already awarded persist; high score & elapsed
    /// time keep climbing as before. EnemySpawner.ScheduleNextFinalBoss is
    /// what actually queues the next SlimeGod spawn.
    /// </summary>
    public void ContinueAfterVictory()
    {
        finalBossKilled = false;
        activeFinalBoss = null;
        survivalTimeFrozenSet = false;
        survivalTimeFrozen = 0f;
        // Clear the persisted win flag so a later death doesn't show as a win.
        PlayerPrefs.SetInt(PrefsLastWasWin, 0);
    }

    /// <summary>Resets the run. Call from a "restart" button.</summary>
    public void ResetRun()
    {
        elapsedTime = 0f;
        kills = 0;
        bossKills = 0;
        bonusPoints = 0;
        survivalTimeFrozen = 0f;
        survivalTimeFrozenSet = false;
        finalBossKilled = false;
        activeFinalBoss = null;
        IsRunning = true;
        NewHighScoreThisRun = false;
        savedScores = false;
        HighScore = PlayerPrefs.GetInt(PrefsHighScore, 0);
        startOfRunHighScore = HighScore;
    }

    /// <summary>
    /// Persist the final score and high score so the EndScreen can read them.
    /// Idempotent — called from Update on hero-death and safe to call again.
    /// </summary>
    public void SaveScores()
    {
        if (savedScores) return;
        savedScores = true;

        int finalScore = Score;
        // Compare against the high score at run start, NOT the live one
        // (which we've been updating to match the current score during play).
        bool newHigh = finalScore > startOfRunHighScore;

        PlayerPrefs.SetInt(PrefsLastScore, finalScore);
        if (finalScore > HighScore) HighScore = finalScore;
        PlayerPrefs.SetInt(PrefsHighScore, HighScore);
        PlayerPrefs.SetInt(PrefsWasNewHigh, newHigh ? 1 : 0);
        PlayerPrefs.SetInt(PrefsLastWasWin, finalBossKilled ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>Static convenience for the EndScreen scene where GameManager no longer exists.</summary>
    public static int LoadStoredHighScore() => PlayerPrefs.GetInt(PrefsHighScore, 0);

    /// <summary>Static convenience for the EndScreen scene to read the most recent run's final score.</summary>
    public static int LoadLastFinalScore()  => PlayerPrefs.GetInt(PrefsLastScore, 0);

    /// <summary>True if the most recent run beat the previous high score (set in SaveScores).</summary>
    public static bool LoadLastRunWasNewHigh() => PlayerPrefs.GetInt(PrefsWasNewHigh, 0) != 0;

    /// <summary>True if the most recent run ended in a Slime God kill (used by the EndScreen to switch to the Win readout).</summary>
    public static bool LoadLastRunWasWin() => PlayerPrefs.GetInt(PrefsLastWasWin, 0) != 0;

    /// <summary>Helper for HUD: format the elapsed time as MM:SS.</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int total = Mathf.FloorToInt(seconds);
        int m = total / 60;
        int s = total % 60;
        return string.Format("{0:00}:{1:00}", m, s);
    }
}
