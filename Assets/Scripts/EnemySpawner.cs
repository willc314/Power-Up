using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns 3D enemy prefabs around the Hero during gameplay.
/// This is meant for an endless-survivor / Vampire Survivors-style game where
/// enemies continuously appear near the player and move toward them.
///
/// The spawner does not create the arena. It only references ArenaGenerator so
/// it knows the arena size and avoids spawning enemies outside the map.
///
/// Spawn behavior:
///   * waits for Hero.Instance if the Hero is spawned at runtime
///   * repeatedly spawns waves of enemies
///   * increases enemies per wave over time
///   * keeps a maximum number of alive spawned enemies
///   * places enemies around the Hero between a minimum and maximum distance
///   * raycasts downward so enemies spawn on top of the generated floor
///   * supports weighted spawn chances for each enemy prefab
///
/// Setup:
///   1. Create an empty GameObject named "Spawner".
///   2. Add this script to it.
///   3. Add enemy prefabs into Weighted Enemy Prefabs.
///   4. Set higher Spawn Weight for common enemies and lower Spawn Weight for rare enemies.
///   5. Drag the Arena GameObject into Arena Generator, or leave it empty
///      and the script will find one automatically.
///   6. Leave Hero empty if ArenaGenerator spawns the Hero at runtime.
///
/// Enemy prefab checklist:
///   * Enemy.cs
///   * Rigidbody
///   * Collider
///   * Layer set to Enemy if your weapons use an Enemy layer mask
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class WeightedEnemyPrefab
    {
        [Tooltip("Enemy prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("Higher number = more common. Lower number = rarer. 0 means this enemy will not spawn.")]
        public float spawnWeight = 1f;
    }

    [Header("References")]
    [Tooltip("Enemy prefabs with individual spawn weights. Use this instead of the old equal-chance Enemy Prefabs list.")]
    public WeightedEnemyPrefab[] weightedEnemyPrefabs;

    [Tooltip("Optional reference to ArenaGenerator. Used to keep enemies inside the arena.")]
    public ArenaGenerator arenaGenerator;

    [Tooltip("Optional Hero reference. Usually left empty because the spawner uses Hero.Instance.")]
    public Hero hero;

    [Header("Spawn Timing")]
    [Tooltip("Seconds between spawn waves.")]
    public float spawnInterval = 2f;

    [Tooltip("Enemies spawned per wave at the beginning of the game.")]
    public int startingEnemiesPerWave = 3;

    [Tooltip("Maximum enemies that can spawn in one wave after scaling over time.")]
    public int maxEnemiesPerWave = 20;

    [Tooltip("Every this many seconds, enemies per wave increases by 1.")]
    public float waveIncreaseEverySeconds = 20f;

    [Header("Alive Limit")]
    [Tooltip("Maximum number of living spawned enemies allowed at once.")]
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

    [Tooltip("Layers considered ground for spawn placement. Everything usually works while testing.")]
    public LayerMask groundMask = ~0;

    [Header("Safety")]
    [Tooltip("If true, the spawner checks whether the spawn point is blocked before spawning.")]
    public bool checkSpawnBlocked = false;

    [Tooltip("Radius used for spawn-block checking.")]
    public float spawnCheckRadius = 1f;

    [Tooltip("Layers that block spawning. Do not include the floor unless you want every floor point to count as blocked.")]
    public LayerMask spawnBlockMask = 0;

    [Tooltip("How many random positions to try before giving up on one enemy.")]
    public int maxSpawnAttemptsPerEnemy = 40;

    [Header("Debug")]
    [Tooltip("If true, prints spawn messages and warnings in the Console.")]
    public bool debugLogs = true;

    [Tooltip("If true, shows red/green spawn distance rings around the Hero when Spawner is selected.")]
    public bool drawSpawnRings = true;

    private readonly List<Enemy> aliveEnemies = new List<Enemy>();

    private Transform enemyRoot;
    private float spawnTimer;
    private float elapsedTime;

    private void Start()
    {
        if (arenaGenerator == null)
            arenaGenerator = FindObjectOfType<ArenaGenerator>();

        if (hero == null)
            hero = Hero.Instance;

        GameObject root = new GameObject("__SpawnedEnemies");
        enemyRoot = root.transform;

        spawnTimer = 0f;
        elapsedTime = 0f;

        if (debugLogs)
        {
            Debug.Log("EnemySpawner: started.");
            Debug.Log("EnemySpawner: weighted enemy prefab count = " + (weightedEnemyPrefabs == null ? 0 : weightedEnemyPrefabs.Length));
            Debug.Log("EnemySpawner: arena found = " + (arenaGenerator != null));
            Debug.Log("EnemySpawner: hero found at Start = " + (hero != null));
        }
    }

    private void Update()
    {
        if (hero == null)
            hero = Hero.Instance;

        if (hero == null)
        {
            if (debugLogs)
                Debug.LogWarning("EnemySpawner: waiting for Hero.Instance...");

            return;
        }

        if (hero.IsDead)
            return;

        if (weightedEnemyPrefabs == null || weightedEnemyPrefabs.Length == 0)
        {
            if (debugLogs)
                Debug.LogWarning("EnemySpawner: no weighted enemy prefabs assigned.");

            return;
        }

        CleanupDeadEnemies();

        elapsedTime += Time.deltaTime;
        spawnTimer -= Time.deltaTime;

        if (spawnTimer <= 0f)
        {
            SpawnWave();
            spawnTimer = spawnInterval;
        }
    }

    private void SpawnWave()
    {
        int allowed = maxAliveEnemies - aliveEnemies.Count;

        if (allowed <= 0)
        {
            if (debugLogs)
                Debug.Log("EnemySpawner: max alive enemies reached.");

            return;
        }

        int amount = Mathf.Min(GetCurrentWaveAmount(), allowed);
        int spawned = 0;

        for (int i = 0; i < amount; i++)
        {
            if (TrySpawnEnemy())
                spawned++;
        }

        if (debugLogs)
            Debug.Log("EnemySpawner: spawned " + spawned + " enemies this wave. Alive = " + aliveEnemies.Count);
    }

    private int GetCurrentWaveAmount()
    {
        if (waveIncreaseEverySeconds <= 0f)
            return startingEnemiesPerWave;

        int bonus = Mathf.FloorToInt(elapsedTime / waveIncreaseEverySeconds);
        return Mathf.Clamp(startingEnemiesPerWave + bonus, startingEnemiesPerWave, maxEnemiesPerWave);
    }

    private bool TrySpawnEnemy()
    {
        GameObject prefab = GetRandomEnemyPrefab();

        if (prefab == null)
        {
            if (debugLogs)
                Debug.LogWarning("EnemySpawner: picked null prefab.");

            return false;
        }

        for (int attempt = 0; attempt < maxSpawnAttemptsPerEnemy; attempt++)
        {
            if (!TryGetSpawnPosition(out Vector3 spawnPosition))
                continue;

            GameObject obj = Instantiate(prefab, spawnPosition, Quaternion.identity, enemyRoot);
            obj.name = prefab.name;

            Enemy enemy = obj.GetComponent<Enemy>();

            if (enemy == null)
                enemy = obj.GetComponentInChildren<Enemy>();

            if (enemy == null)
            {
                Debug.LogWarning("EnemySpawner: spawned prefab has no Enemy script: " + prefab.name);
                return false;
            }

            Vector3 toHero = hero.transform.position - obj.transform.position;
            toHero.y = 0f;

            if (toHero.sqrMagnitude > 0.001f)
                obj.transform.rotation = Quaternion.LookRotation(toHero.normalized, Vector3.up);

            aliveEnemies.Add(enemy);

            if (debugLogs)
                Debug.Log("EnemySpawner: spawned " + obj.name + " at " + spawnPosition);

            return true;
        }

        if (debugLogs)
            Debug.LogWarning("EnemySpawner: failed to find valid spawn position after attempts.");

        return false;
    }

    private GameObject GetRandomEnemyPrefab()
    {
        if (weightedEnemyPrefabs == null || weightedEnemyPrefabs.Length == 0)
            return null;

        float totalWeight = 0f;

        for (int i = 0; i < weightedEnemyPrefabs.Length; i++)
        {
            if (weightedEnemyPrefabs[i] == null)
                continue;

            if (weightedEnemyPrefabs[i].prefab == null)
                continue;

            if (weightedEnemyPrefabs[i].spawnWeight <= 0f)
                continue;

            totalWeight += weightedEnemyPrefabs[i].spawnWeight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < weightedEnemyPrefabs.Length; i++)
        {
            if (weightedEnemyPrefabs[i] == null)
                continue;

            if (weightedEnemyPrefabs[i].prefab == null)
                continue;

            if (weightedEnemyPrefabs[i].spawnWeight <= 0f)
                continue;

            currentWeight += weightedEnemyPrefabs[i].spawnWeight;

            if (roll <= currentWeight)
                return weightedEnemyPrefabs[i].prefab;
        }

        return null;
    }

    private bool TryGetSpawnPosition(out Vector3 finalPosition)
    {
        finalPosition = Vector3.zero;

        Vector2 randomCircle = Random.insideUnitCircle;

        if (randomCircle.sqrMagnitude < 0.001f)
            randomCircle = Vector2.right;

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
        {
            finalPosition = hit.point + Vector3.up * spawnAboveGround;
        }
        else
        {
            finalPosition = new Vector3(candidate.x, spawnAboveGround, candidate.z);
        }

        if (checkSpawnBlocked)
        {
            bool blocked = Physics.CheckSphere(
                finalPosition,
                spawnCheckRadius,
                spawnBlockMask,
                QueryTriggerInteraction.Ignore
            );

            if (blocked)
                return false;
        }

        return true;
    }

    private void CleanupDeadEnemies()
    {
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
        {
            if (aliveEnemies[i] == null || aliveEnemies[i].IsDead)
                aliveEnemies.RemoveAt(i);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSpawnRings)
            return;

        Hero drawHero = hero != null ? hero : Hero.Instance;

        if (drawHero == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(drawHero.transform.position, minSpawnDistanceFromHero);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(drawHero.transform.position, maxSpawnDistanceFromHero);
    }
}