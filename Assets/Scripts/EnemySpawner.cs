using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns 3D enemy prefabs around the Hero during gameplay.
///
/// Two spawn tracks:
///
///   1. Regular enemies — continuous, time-scaled spawn RATE.
///      The rate starts low (default ~4 per 10 seconds) and ramps up to a
///      configurable peak over `timeToReachMaxSpawnRate` seconds. The ramp
///      shape is controlled by `spawnRateRampPower` (1 = linear,
///      &gt;1 = slow-then-fast, &lt;1 = fast-then-slow). Each regular enemy gets
///      a linearly-growing HP multiplier so older runs feel tougher.
///
///   2. Bosses — independent timer (default once per minute). Each boss
///      spawned has its base HP multiplied by an exponential factor that
///      grows per minute, so the 5th boss is dramatically tankier than the
///      1st. Use this list for the SlimeKing (or other future bosses).
///
/// Spawn placement is the same for both tracks: random direction around the
/// Hero, between min and max distance, raycasting down so they sit on the
/// generated floor.
///
/// Setup:
///   1. Create an empty GameObject named "Spawner".
///   2. Add this script.
///   3. Drop your normal enemy prefabs into Weighted Enemy Prefabs.
///   4. Drop your SlimeKing (and any future bosses) into Boss Prefabs.
///   5. Optionally tweak the scaling knobs below.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class WeightedEnemyPrefab
    {
        [Tooltip("Enemy prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("Higher = more common. 0 = never picked.")]
        public float spawnWeight = 1f;
    }

    [Header("References")]
    [Tooltip("Regular enemy prefabs picked by weighted random for the continuous spawn track.")]
    public WeightedEnemyPrefab[] weightedEnemyPrefabs;

    [Tooltip("Boss prefabs (e.g. SlimeKing). Spawned independently on the boss timer.")]
    public WeightedEnemyPrefab[] bossPrefabs;

    [Tooltip("Optional reference to ArenaGenerator. Used to keep enemies inside the arena.")]
    public ArenaGenerator arenaGenerator;

    [Tooltip("Optional Hero reference. Usually left empty because the spawner uses Hero.Instance.")]
    public Hero hero;

    [Header("Regular Spawn Rate (scales with time)")]
    [Tooltip("Spawns per second at t = 0. 0.4 = ~4 enemies per 10 seconds. Slow start by design.")]
    public float startSpawnsPerSecond = 0.4f;

    [Tooltip("Spawns per second once the ramp reaches its peak.")]
    public float maxSpawnsPerSecond = 8f;

    [Tooltip("Seconds for the spawn rate to climb from start to max.")]
    public float timeToReachMaxSpawnRate = 600f;

    [Tooltip("Curve shape for the ramp. 1 = linear, 2 = slow start then accelerate, 0.5 = fast start then plateau.")]
    public float spawnRateRampPower = 1.5f;

    [Header("Regular Enemy HP Scaling")]
    [Tooltip("Extra HP multiplier added per minute (linear part). 3 means HP gets +3× base every minute, so at 1 min the linear factor is 4, at 2 min it's 7, at 3 min it's 10.")]
    public float regularHpBonusPerMinute = 3f;

    [Tooltip("Exponential multiplier compounded per minute. 1.5 means HP gets ×1.5 every minute on top of the linear part. At 2 min that's ×2.25 on top of the linear factor; at 5 min that's ×7.59.")]
    public float regularHpExponentialPerMinute = 1.5f;

    [Tooltip("Cap on the total HP multiplier so regular enemies don't become unkillable. Set high (e.g. 1000+) if you want the aggressive scaling to run uncapped.")]
    public float regularHpMaxMultiplier = 1000f;

    [Header("Powerup Drop Scaling")]
    [Tooltip("Multiplier on each enemy's powerUpDropChance when the spawn rate is at startSpawnsPerSecond. Higher = more powerups early in the run.")]
    public float dropChanceMulAtStart = 4f;
    [Tooltip("Multiplier on each enemy's powerUpDropChance once the ramp reaches maxSpawnsPerSecond. Lower = fewer drops during peak waves so the player isn't spammed.")]
    public float dropChanceMulAtPeak = 0.4f;

    [Header("Boss Spawn (SlimeKing etc.)")]
    [Tooltip("Seconds before the FIRST boss spawn. Lets the player ramp up before facing one.")]
    public float bossFirstSpawnDelay = 60f;

    [Tooltip("Seconds between bosses after the first. 60 = one boss per minute.")]
    public float bossSpawnInterval = 60f;

    [Tooltip("Boss HP grows exponentially per spawn: each boss is this multiple of the previous boss's multiplier. 2 → 1st boss=2× HP, 2nd=4×, 3rd=8×, 4th=16×. 1.5 → 1.5×, 2.25×, 3.375×, 5.06×, ...")]
    public float bossHpMultiplierPerSpawn = 2f;

    [Header("Final Boss (SlimeGod)")]
    [Tooltip("Prefab spawned once at finalBossSpawnTime. Should have an Enemy component with behavior=SlimeGod and a SlimeGod component.")]
    public GameObject finalBossPrefab;
    [Tooltip("Seconds into the run when the final boss spawns. Default 480 = 8 minutes.")]
    public float finalBossSpawnTime = 480f;
    [Tooltip("Distance the final boss spawns away from the player.")]
    public float finalBossSpawnDistance = 8f;
    [Tooltip("Visual radius of the spawn shockwave that wipes all other enemies.")]
    public float finalBossShockwaveRadius = 60f;
    [Tooltip("Camera shake amplitude when the final boss spawns.")]
    public float finalBossShockwaveShake = 0.9f;
    [Tooltip("Camera shake duration when the final boss spawns.")]
    public float finalBossShockwaveShakeDuration = 1.2f;
    [Tooltip("How many existing enemies KillSilently runs per frame during the boss-spawn wipe. Higher = wipe finishes sooner but the spawn frame may stutter; lower = smoother spawn but wipe takes longer.")]
    public int finalBossSpawnWipeKillsPerFrame = 8;
    [Tooltip("If true, instantiate the final boss prefab once at scene load (far offscreen, with all MonoBehaviours disabled) so its meshes / materials / shaders are compiled before the real spawn. Eliminates the first-time-instantiate hitch at the 8-minute mark. Costs nothing if the prefab is null.")]
    public bool prewarmFinalBossPrefab = true;
    [Tooltip("After the player picks Continue on the Win Menu, this many minutes from the moment of the previous boss kill before the next final boss spawns.")]
    public float finalBossRespawnDelayMinutes = 4f;
    [Tooltip("EXTRA exponential HP multiplier applied to the final boss on top of bossHpMultiplierPerSpawn. Effective HP scale = (bossMul × extraMul)^finalBossesSpawned. Defaults to 1.5 so endless mode ramps faster than the regular boss curve. Set to 1 to disable the extra stack.")]
    public float finalBossExtraHpMultiplierPerSpawn = 1.5f;
    [Tooltip("EXPONENTIAL damage multiplier applied to the final boss per previous summon. Stacks multiplicatively with the linear bonus below — effective dmg multiplier = (1 + linearBonus × n) × extraMul^n. Default 1.3 → ×1, ×1.3, ×1.69, ×2.20, …. Set to 1 to disable.")]
    public float finalBossExtraDamageMultiplierPerSpawn = 1.3f;
    [Tooltip("Linear ATTACK-DAMAGE bonus added per previous final boss kill. Effective dmg multiplier = 1 + bonus × finalBossesSpawned. Default +0.5 → 1×, 1.5×, 2×, 2.5×, …")]
    public float finalBossDamageBonusPerKill = 0.5f;
    [Tooltip("Linear PROJECTILE-COUNT bonus added per previous final boss kill. Effective count multiplier = 1 + bonus × finalBossesSpawned. Spam / aerial / homing fans all get this many extra arrows.")]
    public float finalBossProjectileBonusPerKill = 0.5f;
    [Tooltip("Linear ATTACK-SPEED bonus added per previous final boss kill. Effective rate multiplier = 1 + bonus × finalBossesSpawned. Higher = shorter cooldowns / intervals between attacks. Default +0.3 → 1×, 1.3×, 1.6×, …")]
    public float finalBossAttackSpeedBonusPerKill = 0.3f;

    [Header("Alive Limit")]
    [Tooltip("Maximum number of living spawned enemies (regular + bosses) at once.")]
    public int maxAliveEnemies = 100;

    [Header("Spawn Area")]
    [Tooltip("Closest distance from the Hero where enemies may spawn.")]
    public float minSpawnDistanceFromHero = 5f;

    [Tooltip("Farthest distance from the Hero where enemies may spawn.")]
    public float maxSpawnDistanceFromHero = 15f;

    [Tooltip("How far enemies must stay away from the arena edge.")]
    public float arenaEdgeMargin = 3f;

    [Header("Ground Placement")]
    [Tooltip("Height above the spawn point where the downward raycast begins.")]
    public float raycastStartHeight = 20f;

    [Tooltip("Small upward offset so enemies spawn slightly above the ground.")]
    public float spawnAboveGround = 0.15f;

    [Tooltip("Layers considered ground for spawn placement.")]
    public LayerMask groundMask = ~0;

    [Header("Safety")]
    [Tooltip("If true, the spawner checks whether the spawn point is blocked before spawning.")]
    public bool checkSpawnBlocked = false;

    [Tooltip("Radius used for spawn-block checking.")]
    public float spawnCheckRadius = 1f;

    [Tooltip("Layers that block spawning. Do not include the floor.")]
    public LayerMask spawnBlockMask = 0;

    [Tooltip("How many random positions to try before giving up on one enemy.")]
    public int maxSpawnAttemptsPerEnemy = 40;

    [Header("Debug")]
    public bool debugLogs = true;
    public bool drawSpawnRings = true;

    public static EnemySpawner Instance { get; private set; }

    /// <summary>
    /// Read-only view of every currently alive enemy spawned by this spawner.
    /// Used by homing projectiles (e.g. fully-charged bow shots) to enumerate
    /// targets directly instead of relying on Physics.OverlapSphere — which
    /// can miss enemies when the buffer fills with non-enemy colliders that
    /// share the weapon's layer mask (cacti, terrain, etc.).
    /// </summary>
    public IReadOnlyList<Enemy> AliveEnemies => aliveEnemies;

    private readonly List<Enemy> aliveEnemies = new List<Enemy>();
    private Transform enemyRoot;
    private float elapsedTime;
    private float spawnAccumulator;   // fractional spawns banked frame-to-frame
    private float bossSpawnTimer;     // counts down to the next boss
    private int   bossesSpawned;      // how many bosses have spawned this run
    private bool  finalBossSpawned;   // true after the next-scheduled SlimeGod has been spawned (reset on Continue)
    private bool  finalBossActive;    // true while a SlimeGod is alive
    private int   finalBossesSpawned; // running count for HP scaling — never resets on Continue

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (arenaGenerator == null) arenaGenerator = FindObjectOfType<ArenaGenerator>();
        if (hero == null) hero = Hero.Instance;

        // Apply the difficulty preset before any spawning happens. This
        // overrides the inspector defaults so Easy/Normal/Hard scale the
        // run consistently with the player's choice.
        if (GameSettings.Instance != null)
        {
            var p = GameSettings.Instance.GetActivePreset();
            regularHpBonusPerMinute  = p.regularHpBonusPerMinute;
            bossSpawnInterval        = p.bossSpawnInterval;
            bossHpMultiplierPerSpawn = p.bossHpMultiplierPerSpawn;
        }

        enemyRoot = new GameObject("__SpawnedEnemies").transform;

        elapsedTime = 0f;
        spawnAccumulator = 0f;
        bossSpawnTimer = bossFirstSpawnDelay;

        if (debugLogs)
        {
            Debug.Log($"EnemySpawner: started. Regular start rate = {startSpawnsPerSecond}/s, peak = {maxSpawnsPerSecond}/s after {timeToReachMaxSpawnRate}s.");
            Debug.Log($"EnemySpawner: first boss at {bossFirstSpawnDelay}s, then every {bossSpawnInterval}s.");
        }

        if (prewarmFinalBossPrefab && finalBossPrefab != null)
            StartCoroutine(WarmUpFinalBossPrefab());
    }

    /// <summary>
    /// Instantiate the final boss prefab once at scene load to force shader /
    /// material / mesh-buffer compilation. The instance is parked far off-map
    /// with every MonoBehaviour disabled so it can't run patterns or affect
    /// the run, lives for one render frame so its renderers actually draw
    /// (which is what triggers the GPU compile), then gets destroyed.
    /// </summary>
    private System.Collections.IEnumerator WarmUpFinalBossPrefab()
    {
        // Wait one frame so GameManager / Hero / WinMenu have all done their
        // own Awake/OnEnable before our warmup briefly registers itself.
        yield return null;

        Vector3 farAway = new Vector3(99999f, -9999f, 99999f);
        GameObject warmup = Instantiate(finalBossPrefab, farAway, Quaternion.identity);
        warmup.name = finalBossPrefab.name + "_Warmup";

        // SlimeGod.OnEnable registered itself as the active final boss; undo
        // that so the HUD / GameManager don't think the boss is already alive.
        if (GameManager.Instance != null) GameManager.Instance.NotifyFinalBossDespawned();

        // Disable every MonoBehaviour on the warmup so Start (and the master
        // / beam coroutines it would kick off) never runs. Renderers and the
        // Animator remain enabled — they're not MonoBehaviours, so they keep
        // running long enough to compile shaders and instantiate materials.
        foreach (var b in warmup.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (b != null) b.enabled = false;
        }

        // Make sure the warmup's Rigidbody can't physically interact during
        // its single frame of life (e.g. fall through ground colliders that
        // happen to extend out to the offscreen position).
        foreach (var rb in warmup.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
        }
        // And belt-and-suspenders: turn colliders off too.
        foreach (var col in warmup.GetComponentsInChildren<Collider>(true))
        {
            if (col != null) col.enabled = false;
        }

        // Live for one render frame so the GPU actually draws the meshes and
        // compiles the shader variants we'll need at real spawn time.
        yield return new WaitForEndOfFrame();

        Destroy(warmup);

        if (debugLogs) Debug.Log("EnemySpawner: final boss prefab warmed up.");
    }

    private void Update()
    {
        if (hero == null) hero = Hero.Instance;
        if (hero == null || hero.IsDead) return;

        CleanupDeadEnemies();
        elapsedTime += Time.deltaTime;

        // --- Final boss (one-shot at finalBossSpawnTime) ---
        if (!finalBossSpawned && finalBossPrefab != null && elapsedTime >= finalBossSpawnTime)
        {
            SpawnFinalBoss();
        }

        // The final boss takes over the arena: stop spawning anything else
        // while it's alive (regular waves AND the timed-boss track).
        if (finalBossActive) return;

        // --- Regular spawn track ---
        if (HasRegularPool())
        {
            float rate = GetCurrentSpawnsPerSecond();
            spawnAccumulator += rate * Time.deltaTime;

            // Drain the accumulator one spawn at a time. The while loop
            // handles any rate (very low <1/s and very high >1/frame).
            while (spawnAccumulator >= 1f && aliveEnemies.Count < maxAliveEnemies)
            {
                spawnAccumulator -= 1f;
                TrySpawn(boss: false);
            }
            // Keep the accumulator from drifting too high if we're at the alive cap.
            if (aliveEnemies.Count >= maxAliveEnemies && spawnAccumulator > 1f)
                spawnAccumulator = 1f;
        }

        // --- Boss spawn track ---
        if (HasBossPool())
        {
            bossSpawnTimer -= Time.deltaTime;
            if (bossSpawnTimer <= 0f)
            {
                if (aliveEnemies.Count < maxAliveEnemies) TrySpawn(boss: true);
                bossSpawnTimer = bossSpawnInterval;
            }
        }
    }

    /// <summary>
    /// Spawns the SlimeGod once 8 minutes have elapsed. Wipes every other
    /// enemy off the arena via an expanding visual shockwave and stops further
    /// regular/boss spawns until the boss dies.
    /// </summary>
    private void SpawnFinalBoss()
    {
        finalBossSpawned = true;

        // Pick a spawn position around the player.
        Vector2 ring = Random.insideUnitCircle.normalized;
        if (ring.sqrMagnitude < 0.001f) ring = Vector2.right;
        Vector3 spawnPos = hero.transform.position + new Vector3(ring.x, 0f, ring.y) * finalBossSpawnDistance;
        spawnPos.y = hero.transform.position.y;
        if (arenaGenerator != null)
        {
            float half = arenaGenerator.arenaSize * 0.5f - arenaEdgeMargin;
            spawnPos.x = Mathf.Clamp(spawnPos.x, -half, half);
            spawnPos.z = Mathf.Clamp(spawnPos.z, -half, half);
        }

        // Visual shockwave centered on the boss spawn point.
        BossShockwave.Spawn(spawnPos, finalBossShockwaveRadius, new Color(1f, 0.25f, 0.25f, 1f), 0.7f);
        if (Camera.main != null)
        {
            var cf = Camera.main.GetComponent<CameraFollow>();
            if (cf != null) cf.Shake(finalBossShockwaveShake, finalBossShockwaveShakeDuration);
        }

        // Snapshot the existing enemies (excluding null/dead) and clear the
        // list — the boss is added below. The actual wipe happens over the
        // next few frames in a coroutine so we don't drop a 1k-particle frame
        // spike right when the player needs to read the boss spawn.
        List<Enemy> toWipe = new List<Enemy>(aliveEnemies.Count);
        for (int i = 0; i < aliveEnemies.Count; i++)
        {
            Enemy e = aliveEnemies[i];
            if (e != null && !e.IsDead) toWipe.Add(e);
        }
        aliveEnemies.Clear();
        if (toWipe.Count > 0) StartCoroutine(WipeEnemiesGradually(toWipe));

        // Spawn the boss FIRST so it's visible immediately.
        GameObject boss = Instantiate(finalBossPrefab, spawnPos, Quaternion.identity, enemyRoot);
        boss.name = finalBossPrefab.name;
        Enemy bossEnemy = boss.GetComponent<Enemy>() ?? boss.GetComponentInChildren<Enemy>();
        // Pre-compute everything BEFORE applying so the same numbers can go
        // both into the boss instance and the debug log below.
        float hpMul = GetCurrentFinalBossHpMultiplier();
        int   priorKills = finalBossesSpawned;
        // Exponential per-spawn damage multiplier — stacks multiplicatively
        // on top of the linear bonus so endless mode ramps faster than the
        // linear curve alone. extraMul^0 = 1, so the FIRST spawn gets x1
        // (the linear bonus is also 0 on the first spawn), and subsequent
        // spawns compound by extraMul each.
        float extraDmgMul = Mathf.Pow(Mathf.Max(0.01f, finalBossExtraDamageMultiplierPerSpawn), priorKills);
        float dmgMul   = (1f + Mathf.Max(0f, finalBossDamageBonusPerKill)      * priorKills) * extraDmgMul;
        float projMul  = 1f + Mathf.Max(0f, finalBossProjectileBonusPerKill)  * priorKills;
        float speedMul = 1f + Mathf.Max(0f, finalBossAttackSpeedBonusPerKill) * priorKills;

        if (bossEnemy != null)
        {
            // Always call ScaleHP — the n+1 exponent guarantees the first
            // spawn is already > 1×, and a no-op multiplier is cheap.
            bossEnemy.ScaleHP(hpMul);

            // Linear damage / projectile / attack-speed multipliers stacked
            // on top. The SlimeGod controller reads these at attack time
            // (ScaledDamage / ScaledCount / ScaledInterval).
            SlimeGod sg = bossEnemy.GetComponent<SlimeGod>();
            if (sg != null)
            {
                sg.attackDamageMultiplier    = dmgMul;
                sg.projectileCountMultiplier = projMul;
                sg.attackSpeedMultiplier     = speedMul;
                // After the first summon, projectile counts scale up via
                // projMul + extraProjectileCount and the per-projectile VFX
                // overrides (FireIce trails / Gabriel Aguiar trails) become
                // a frame-rate problem. Suppress the visualPrefab on every
                // projectile this respawn fires; gameplay is unaffected.
                sg.suppressProjectileVisuals = priorKills > 0;
            }

            aliveEnemies.Add(bossEnemy);
        }

        finalBossActive = true;
        finalBossesSpawned++;

        if (debugLogs)
            Debug.Log($"EnemySpawner: SlimeGod spawn #{finalBossesSpawned} at t={elapsedTime:F1}s, " +
                      $"bossesSpawned={bossesSpawned}, baseExp={bossesSpawned + priorKills + 1}, extraExp={priorKills + 1}, " +
                      $"HP×{hpMul:F2}, DMG×{dmgMul:F2}, PROJ×{projMul:F2}, SPEED×{speedMul:F2}, " +
                      $"resulting maxHP={bossEnemy?.MaxHP}.");
    }

    /// <summary>
    /// HP multiplier the NEXT final boss spawn will use. The base
    /// (bossHpMultiplierPerSpawn) curve CONTINUES from the SlimeKing series —
    /// every regular boss AND every previous final boss spawn pushes the
    /// exponent up. The extra multiplier only stacks once per final-boss
    /// spawn so the player sees an obvious jump on each Continue cycle.
    ///
    /// Effective exponent on bossHpMultiplierPerSpawn:
    ///     bossesSpawned + finalBossesSpawned + 1
    /// Effective exponent on finalBossExtraHpMultiplierPerSpawn:
    ///     finalBossesSpawned + 1
    ///
    /// Example with bossMul=2, extraMul=1.5, default 60s boss interval and
    /// 8-minute final boss spawn time → ~7 SlimeKings have spawned by then,
    /// so the 1st final boss gets 2^8 × 1.5 = 384× base HP.
    /// </summary>
    public float GetCurrentFinalBossHpMultiplier() => GetFinalBossHpMultiplierAt(finalBossesSpawned);

    private float GetFinalBossHpMultiplierAt(int finalPriorKills)
    {
        float baseMul  = Mathf.Max(0.01f, bossHpMultiplierPerSpawn);
        float extraMul = Mathf.Max(0.01f, finalBossExtraHpMultiplierPerSpawn);
        int   baseExp  = bossesSpawned + finalPriorKills + 1;
        int   extraExp = finalPriorKills + 1;
        return Mathf.Pow(baseMul, baseExp) * Mathf.Pow(extraMul, extraExp);
    }

    /// <summary>
    /// Spread the mass-kill of existing enemies across multiple frames so the
    /// boss-spawn frame doesn't take a giant spike from N simultaneous
    /// KillSilently calls (each spawning particles + scheduling Destroy).
    /// </summary>
    private System.Collections.IEnumerator WipeEnemiesGradually(List<Enemy> targets)
    {
        int budget = Mathf.Max(1, finalBossSpawnWipeKillsPerFrame);
        int processed = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            Enemy e = targets[i];
            if (e == null || e.IsDead) continue;
            // Skip particles for everything past the first batch — the
            // arena-wide visual shockwave already sells the wipe; per-enemy
            // bursts on later batches are redundant cost.
            e.KillSilently(emitParticles: processed < budget);
            processed++;
            if (processed % budget == 0) yield return null;
        }
    }

    /// <summary>
    /// Called by the Win Menu's Continue button. Resets the one-shot final
    /// boss flags and pushes the next spawn time out by
    /// finalBossRespawnDelayMinutes minutes from now. The HP-scale counter
    /// is NOT reset — each subsequent boss keeps stacking finalBossHpMultiplier.
    /// Regular enemy spawning resumes automatically (since finalBossActive is
    /// already false once the boss died).
    /// </summary>
    public void ScheduleNextFinalBoss()
    {
        finalBossSpawned = false;
        finalBossActive  = false;
        finalBossSpawnTime = elapsedTime + Mathf.Max(0f, finalBossRespawnDelayMinutes) * 60f;
        if (debugLogs) Debug.Log($"EnemySpawner: next SlimeGod scheduled for t={finalBossSpawnTime:F1}s (HP will be ×{GetCurrentFinalBossHpMultiplier():F2}).");
    }

    // ---------- Spawn-rate ramp ----------

    /// <summary>
    /// Returns the current regular-enemy spawn rate in spawns/second,
    /// shaped by spawnRateRampPower along the [start, max] interval.
    /// </summary>
    public float GetCurrentSpawnsPerSecond()
    {
        if (timeToReachMaxSpawnRate <= 0.01f) return maxSpawnsPerSecond;

        float t01 = Mathf.Clamp01(elapsedTime / timeToReachMaxSpawnRate);
        float power = Mathf.Max(0.01f, spawnRateRampPower);
        float curved = Mathf.Pow(t01, power);
        return Mathf.Lerp(startSpawnsPerSecond, maxSpawnsPerSecond, curved);
    }

    /// <summary>
    /// HP multiplier applied to regular-enemy spawns at the current time.
    /// Combines a linear ramp (1 + bonusPerMinute × minutes) with an exponential
    /// ramp (exponentialPerMinute ^ minutes), capped by regularHpMaxMultiplier.
    /// At t=0 both factors are 1, so spawns use base HP at game start.
    /// </summary>
    public float GetCurrentRegularHpMultiplier()
    {
        float minutes = elapsedTime / 60f;
        float linear = 1f + regularHpBonusPerMinute * minutes;
        float expo = (regularHpExponentialPerMinute > 0f)
            ? Mathf.Pow(regularHpExponentialPerMinute, minutes)
            : 1f;
        float mul = linear * expo;
        return Mathf.Min(regularHpMaxMultiplier, mul);
    }

    /// <summary>
    /// HP multiplier applied to the NEXT boss spawn. Doubles (or whatever
    /// bossHpMultiplierPerSpawn is) on every successive boss, so spawn #N
    /// is bossHpMultiplierPerSpawn ^ N relative to the boss prefab's base HP.
    /// </summary>
    public float GetCurrentBossHpMultiplier()
    {
        float baseMul = Mathf.Max(0.01f, bossHpMultiplierPerSpawn);
        return Mathf.Pow(baseMul, bossesSpawned + 1);
    }

    /// <summary>
    /// Multiplier on each enemy's powerUpDropChance, lerped from
    /// dropChanceMulAtStart down to dropChanceMulAtPeak over
    /// timeToReachMaxSpawnRate seconds. The intent is to keep total
    /// powerups-per-second roughly steady regardless of spawn rate: more
    /// drops per kill early when kills are rare, fewer per kill later when
    /// the screen is full of enemies.
    /// </summary>
    public float GetCurrentDropChanceMultiplier()
    {
        if (timeToReachMaxSpawnRate <= 0.01f) return dropChanceMulAtPeak;
        float t01 = Mathf.Clamp01(elapsedTime / timeToReachMaxSpawnRate);
        return Mathf.Lerp(dropChanceMulAtStart, dropChanceMulAtPeak, t01);
    }

    // ---------- Spawn ----------

    private bool TrySpawn(bool boss)
    {
        var pool = boss ? bossPrefabs : weightedEnemyPrefabs;
        GameObject prefab = GetWeightedRandom(pool);
        if (prefab == null)
        {
            if (debugLogs) Debug.LogWarning($"EnemySpawner: pool {(boss ? "boss" : "regular")} is empty or all weights are zero.");
            return false;
        }

        for (int attempt = 0; attempt < maxSpawnAttemptsPerEnemy; attempt++)
        {
            if (!TryGetSpawnPosition(out Vector3 spawnPosition)) continue;

            GameObject obj = Instantiate(prefab, spawnPosition, Quaternion.identity, enemyRoot);
            obj.name = prefab.name;

            Enemy enemy = obj.GetComponent<Enemy>() ?? obj.GetComponentInChildren<Enemy>();
            if (enemy == null)
            {
                Debug.LogWarning("EnemySpawner: spawned prefab has no Enemy script: " + prefab.name);
                return false;
            }

            // HP scaling. Regular = linear in time, boss = exponential in
            // spawn count. Awake on the spawned enemy already ran during
            // Instantiate above, so ScaleHP multiplies currentHP and maxHP
            // together.
            float hpMul = boss ? GetCurrentBossHpMultiplier() : GetCurrentRegularHpMultiplier();
            if (hpMul != 1f) enemy.ScaleHP(hpMul);

            // Bump the boss counter AFTER applying the multiplier so the next
            // call sees the next exponent step. Spawn #N gets param^N HP.
            if (boss) bossesSpawned++;

            // Face the Hero on spawn so animations look right.
            Vector3 toHero = hero.transform.position - obj.transform.position;
            toHero.y = 0f;
            if (toHero.sqrMagnitude > 0.001f)
                obj.transform.rotation = Quaternion.LookRotation(toHero.normalized, Vector3.up);

            aliveEnemies.Add(enemy);

            if (debugLogs)
                Debug.Log($"EnemySpawner: spawned {obj.name}{(boss ? " [BOSS]" : "")} at t={elapsedTime:F1}s. HP×{hpMul:F2}. Alive={aliveEnemies.Count}.");

            return true;
        }

        if (debugLogs)
            Debug.LogWarning("EnemySpawner: failed to find valid spawn position after attempts.");

        return false;
    }

    private bool HasRegularPool()
    {
        return PoolHasAnyValid(weightedEnemyPrefabs);
    }

    private bool HasBossPool()
    {
        return PoolHasAnyValid(bossPrefabs);
    }

    private static bool PoolHasAnyValid(WeightedEnemyPrefab[] pool)
    {
        if (pool == null) return false;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null && pool[i].prefab != null && pool[i].spawnWeight > 0f) return true;
        }
        return false;
    }

    private static GameObject GetWeightedRandom(WeightedEnemyPrefab[] pool)
    {
        if (pool == null || pool.Length == 0) return null;

        float total = 0f;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null || pool[i].prefab == null || pool[i].spawnWeight <= 0f) continue;
            total += pool[i].spawnWeight;
        }
        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float running = 0f;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null || pool[i].prefab == null || pool[i].spawnWeight <= 0f) continue;
            running += pool[i].spawnWeight;
            if (roll <= running) return pool[i].prefab;
        }
        return null;
    }

    private bool TryGetSpawnPosition(out Vector3 finalPosition)
    {
        finalPosition = Vector3.zero;

        Vector2 randomCircle = Random.insideUnitCircle;
        if (randomCircle.sqrMagnitude < 0.001f) randomCircle = Vector2.right;
        randomCircle.Normalize();

        float distance = Random.Range(minSpawnDistanceFromHero, maxSpawnDistanceFromHero);
        Vector3 candidate = hero.transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y) * distance;

        if (arenaGenerator != null)
        {
            float half = arenaGenerator.arenaSize * 0.5f - arenaEdgeMargin;
            if (candidate.x < -half || candidate.x > half || candidate.z < -half || candidate.z > half)
                return false;
        }

        Vector3 rayStart = new Vector3(candidate.x, raycastStartHeight, candidate.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastStartHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
            finalPosition = hit.point + Vector3.up * spawnAboveGround;
        else
            finalPosition = new Vector3(candidate.x, spawnAboveGround, candidate.z);

        if (checkSpawnBlocked)
        {
            bool blocked = Physics.CheckSphere(finalPosition, spawnCheckRadius, spawnBlockMask, QueryTriggerInteraction.Ignore);
            if (blocked) return false;
        }
        return true;
    }

    private void CleanupDeadEnemies()
    {
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
        {
            Enemy e = aliveEnemies[i];
            if (e == null || e.IsDead)
            {
                if (e != null && e.behavior == Enemy.Behavior.SlimeGod)
                    finalBossActive = false;
                aliveEnemies.RemoveAt(i);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSpawnRings) return;
        Hero drawHero = hero != null ? hero : Hero.Instance;
        if (drawHero == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(drawHero.transform.position, minSpawnDistanceFromHero);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(drawHero.transform.position, maxSpawnDistanceFromHero);
    }
}
