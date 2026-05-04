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

    [Header("Regular Enemy HP Scaling (linear)")]
    [Tooltip("Extra HP multiplier added per minute. 0.5 means HP is 1.5x at t=1min, 2x at t=2min, etc.")]
    public float regularHpBonusPerMinute = 0.5f;

    [Tooltip("Cap on the HP multiplier so regular enemies don't become unkillable.")]
    public float regularHpMaxMultiplier = 20f;

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

    private readonly List<Enemy> aliveEnemies = new List<Enemy>();
    private Transform enemyRoot;
    private float elapsedTime;
    private float spawnAccumulator;   // fractional spawns banked frame-to-frame
    private float bossSpawnTimer;     // counts down to the next boss
    private int   bossesSpawned;      // how many bosses have spawned this run
    private bool  finalBossSpawned;   // true after SlimeGod has been spawned this run
    private bool  finalBossActive;    // true while a SlimeGod is alive

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

        enemyRoot = new GameObject("__SpawnedEnemies").transform;

        elapsedTime = 0f;
        spawnAccumulator = 0f;
        bossSpawnTimer = bossFirstSpawnDelay;

        if (debugLogs)
        {
            Debug.Log($"EnemySpawner: started. Regular start rate = {startSpawnsPerSecond}/s, peak = {maxSpawnsPerSecond}/s after {timeToReachMaxSpawnRate}s.");
            Debug.Log($"EnemySpawner: first boss at {bossFirstSpawnDelay}s, then every {bossSpawnInterval}s.");
        }
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

        // Wipe every existing enemy off the arena.
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
        {
            if (aliveEnemies[i] != null && !aliveEnemies[i].IsDead)
                aliveEnemies[i].KillSilently();
        }
        aliveEnemies.Clear();

        // Spawn the boss.
        GameObject boss = Instantiate(finalBossPrefab, spawnPos, Quaternion.identity, enemyRoot);
        boss.name = finalBossPrefab.name;
        Enemy bossEnemy = boss.GetComponent<Enemy>() ?? boss.GetComponentInChildren<Enemy>();
        if (bossEnemy != null) aliveEnemies.Add(bossEnemy);

        finalBossActive = true;

        if (debugLogs) Debug.Log($"EnemySpawner: SlimeGod spawned at t={elapsedTime:F1}s, position={spawnPos}.");
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

    /// <summary>HP multiplier applied to regular-enemy spawns at the current time.</summary>
    public float GetCurrentRegularHpMultiplier()
    {
        float minutes = elapsedTime / 60f;
        float mul = 1f + regularHpBonusPerMinute * minutes;
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
