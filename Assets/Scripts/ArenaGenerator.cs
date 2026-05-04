using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a square arena at runtime:
///   * a flat floor (auto-sized Plane)
///   * four invisible BoxCollider walls around the perimeter
///   * randomly scattered obstacle prefabs (trees, rocks, shrubs, ...)
///
/// Obstacles are placed with simple rejection sampling so they keep a minimum
/// spacing from each other AND from the player spawn point. The layout
/// re-randomizes every time the scene starts unless you set a fixed seed.
///
/// Setup:
///   1. Create an empty GameObject in your scene named "Arena" and add this script.
///   2. Drag your tree/rock prefabs from Assets/Polytope Studio/Lowpoly_Environments/Prefabs
///      into the Obstacle Prefabs list.
///   3. (Optional) Assign Floor Material if you want the floor textured.
///   4. (Optional) Drag the Hero into Player Spawn Reference, OR leave it empty
///      and the script will look for a Hero in the scene at Start.
///
/// Note about colliders: trees/rocks need a Collider so the player can't walk
/// through them. The Polytope prefabs ship with mesh colliders on most pieces.
/// If a particular prefab has none, add a CapsuleCollider/BoxCollider to the
/// prefab itself (one-time fix) and the spawner will inherit it automatically.
/// </summary>
public class ArenaGenerator : MonoBehaviour
{
    [Header("Arena")]
    [Tooltip("Edge length of the square arena, in Unity units (meters). Default 200 = 200x200.")]
    public float arenaSize = 200f;
    [Tooltip("Height of the invisible walls. Tall enough that enemies/projectiles can't pass over.")]
    public float wallHeight = 10f;
    [Tooltip("Thickness of the wall colliders.")]
    public float wallThickness = 2f;
    [Tooltip("Optional material for the floor. Leave empty to use Unity's default.")]
    public Material floorMaterial;

    [Header("Obstacles")]
    [Tooltip("Drag tree, rock, and shrub prefabs in here. The spawner picks one at random for each placement.")]
    public GameObject[] obstaclePrefabs;
    [Tooltip("Number of obstacles to attempt to place. Around 0.005-0.01 per square unit feels good (so ~200-400 for a 200x200 arena).")]
    public int obstacleCount = 300;
    [Tooltip("Minimum distance allowed between two obstacle centers.")]
    public float minObstacleSpacing = 4f;
    [Tooltip("How close obstacles can get to the arena edge.")]
    public float edgeMargin = 3f;
    [Tooltip("No obstacles will spawn within this radius of the player spawn point.")]
    public float playerClearRadius = 8f;
    [Tooltip("Each obstacle is randomly scaled uniformly between these values.")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.4f);
    [Tooltip("How many times to retry placing a single obstacle before giving up. Higher = denser packing but slower generation.")]
    public int maxPlacementAttempts = 20;

    [Header("Auto Colliders")]
    [Tooltip("If true, any spawned obstacle that has no Collider gets a CapsuleCollider added automatically. This is useful when obstacle prefabs ship without colliders.")]
    public bool autoAddColliders = true;

    [Tooltip("Prefabs in this list spawn WITHOUT auto-added colliders, even if Auto Add Colliders is on. Use for purely cosmetic obstacles the player should walk through (e.g. small rocks, grass).")]
    public GameObject[] noColliderPrefabs;

    [Tooltip("Optional layer assigned to generated obstacles. Use an Obstacle layer so enemies can detect and avoid structures.")]
    public LayerMask obstacleLayer;

    [Tooltip("Capsule radius multiplier based on the obstacle footprint. Lower values make trees only block around the trunk/stump instead of their leaves/branches.")]
    [Range(0.03f, 1.0f)] public float colliderRadiusFactor = 0.12f;

    [Tooltip("Capsule height multiplier based on the obstacle height. Lower values make only the bottom trunk/stump block movement.")]
    [Range(0.1f, 2.0f)] public float colliderHeightFactor = 0.35f;

    [Tooltip("Minimum automatic obstacle collider height.")]
    public float colliderMinHeight = 1.5f;

    [Tooltip("Minimum automatic obstacle collider radius.")]
    public float colliderMinRadius = 0.3f;

    [Header("Player")]
    [Tooltip("Hero prefab to instantiate when the arena is generated. Drag your Hero prefab in here (it must have the Hero script attached).")]
    public Hero heroPrefab;
    [Tooltip("World position to spawn the hero at.")]
    public Vector3 playerSpawnPoint = Vector3.zero;
    [Tooltip("If true, an existing Hero already in the scene will be reused (and moved to the spawn point) instead of spawning a new one.")]
    public bool reuseExistingHero = true;

    [Header("Camera")]
    [Tooltip("Optional. If set, this CameraFollow's target will be assigned to the spawned hero so the camera tracks it.")]
    public CameraFollow cameraFollow;

    [Header("Randomization")]
    [Tooltip("If non-zero, this seed is used so the same number always produces the same layout. Leave 0 for true random per run.")]
    public int randomSeed = 0;

    // Container that holds everything we spawn so we can wipe it cleanly.
    private Transform generatedRoot;

    private void Start()
    {
        Generate();
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        ClearGenerated();
        SeedRandom();

        BuildFloor();
        BuildWalls();
        ScatterObstacles(playerSpawnPoint);
        SpawnOrPlaceHero();
    }

    private void SpawnOrPlaceHero()
    {
        Hero hero = null;

        if (reuseExistingHero) hero = Hero.Instance != null ? Hero.Instance : FindObjectOfType<Hero>();

        if (hero == null)
        {
            if (heroPrefab == null)
            {
                Debug.LogWarning("ArenaGenerator: no Hero in scene and no Hero Prefab assigned — skipping hero spawn.");
                return;
            }
            hero = Instantiate(heroPrefab, playerSpawnPoint + Vector3.up * 0.1f, Quaternion.identity);
            hero.name = heroPrefab.name; // strip "(Clone)" for tidiness
        }
        else
        {
            hero.transform.position = playerSpawnPoint + Vector3.up * 0.1f;
        }

        if (cameraFollow != null) cameraFollow.SetTarget(hero.transform);
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
        Transform existing = transform.Find("__Generated");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
        generatedRoot = null;
    }

    // ---------- internals ----------

    private void SeedRandom()
    {
        if (randomSeed != 0) Random.InitState(randomSeed);
    }

    private Transform Root()
    {
        if (generatedRoot == null)
        {
            GameObject go = new GameObject("__Generated");
            go.transform.SetParent(transform, false);
            generatedRoot = go.transform;
        }
        return generatedRoot;
    }

    private void BuildFloor()
    {
        // Unity's built-in Plane is 10x10 units, so scale = arenaSize / 10.
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(Root(), false);
        floor.transform.localScale = new Vector3(arenaSize / 10f, 1f, arenaSize / 10f);
        floor.transform.position = Vector3.zero;
        if (floorMaterial != null)
        {
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        }
    }

    private void BuildWalls()
    {
        float half = arenaSize * 0.5f;
        // Position walls so their inner face sits exactly on the arena edge.
        float offset = half + wallThickness * 0.5f;
        float y = wallHeight * 0.5f;

        CreateWall("Wall_North", new Vector3(0,  y,  offset), new Vector3(arenaSize + wallThickness * 2f, wallHeight, wallThickness));
        CreateWall("Wall_South", new Vector3(0,  y, -offset), new Vector3(arenaSize + wallThickness * 2f, wallHeight, wallThickness));
        CreateWall("Wall_East",  new Vector3( offset, y, 0), new Vector3(wallThickness, wallHeight, arenaSize));
        CreateWall("Wall_West",  new Vector3(-offset, y, 0), new Vector3(wallThickness, wallHeight, arenaSize));
    }

    private void CreateWall(string name, Vector3 position, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(Root(), false);
        wall.transform.position = position;
        BoxCollider bc = wall.AddComponent<BoxCollider>();
        bc.size = size;
        // No renderer = invisible. If you want to see the walls while debugging,
        // uncomment the next 3 lines:
        // var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // cube.transform.SetParent(wall.transform, false);
        // cube.transform.localScale = size;
    }

    private void ScatterObstacles(Vector3 spawn)
    {
        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0)
        {
            Debug.LogWarning("ArenaGenerator: no obstacle prefabs assigned — skipping scatter.");
            return;
        }

        float half = arenaSize * 0.5f - edgeMargin;
        List<Vector3> placed = new List<Vector3>(obstacleCount);
        float minSpacingSqr = minObstacleSpacing * minObstacleSpacing;
        float clearSqr = playerClearRadius * playerClearRadius;

        Transform parent = new GameObject("Obstacles").transform;
        parent.SetParent(Root(), false);

        int placedCount = 0;
        for (int i = 0; i < obstacleCount; i++)
        {
            Vector3 candidate = Vector3.zero;
            bool found = false;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                float x = Random.Range(-half, half);
                float z = Random.Range(-half, half);
                candidate = new Vector3(x, 0f, z);

                // Reject if too close to the player spawn.
                Vector3 toSpawn = candidate - spawn; toSpawn.y = 0f;
                if (toSpawn.sqrMagnitude < clearSqr) continue;

                // Reject if too close to any previously placed obstacle.
                bool tooClose = false;
                for (int p = 0; p < placed.Count; p++)
                {
                    Vector3 d = placed[p] - candidate;
                    if (d.sqrMagnitude < minSpacingSqr) { tooClose = true; break; }
                }
                if (tooClose) continue;

                found = true;
                break;
            }

            if (!found) continue;

            placed.Add(candidate);
            GameObject prefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];
            if (prefab == null) continue;

            float yaw = Random.Range(0f, 360f);
            float scale = Random.Range(scaleRange.x, scaleRange.y);

            GameObject obj = Instantiate(prefab, candidate, Quaternion.Euler(0f, yaw, 0f), parent);
            obj.transform.localScale = prefab.transform.localScale * scale;
            if (!IsNoColliderPrefab(prefab)) SetupObstacleCollision(obj);
            placedCount++;
        }

        Debug.Log($"ArenaGenerator: placed {placedCount}/{obstacleCount} obstacles (rejected the rest because they couldn't find a free spot).");
    }

    /// <summary>True if the supplied prefab appears in the noColliderPrefabs list.</summary>
    private bool IsNoColliderPrefab(GameObject prefab)
    {
        if (noColliderPrefabs == null || prefab == null) return false;
        for (int i = 0; i < noColliderPrefabs.Length; i++)
            if (noColliderPrefabs[i] == prefab) return true;
        return false;
    }

    /// <summary>
    /// Assigns the obstacle layer and adds a lower CapsuleCollider if the obstacle
    /// has no Collider. The collider is centered near the bottom of the mesh
    /// bounds so tree leaves/branches do not create huge invisible walls.
    /// </summary>
    private void SetupObstacleCollision(GameObject obstacle)
    {
        if (obstacle == null) return;

        if (obstacleLayer.value != 0)
        {
            int layer = GetFirstLayerFromMask(obstacleLayer);
            SetLayerRecursively(obstacle, layer);
        }

        if (!autoAddColliders) return;
        if (obstacle.GetComponentInChildren<Collider>(includeInactive: true) != null) return;

        Renderer[] renderers = obstacle.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        // Combined world-space bounds.
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        // Convert to local space for the CapsuleCollider, accounting for the obstacle's scale.
        Vector3 lossy = obstacle.transform.lossyScale;
        if (Mathf.Abs(lossy.x) < 0.0001f) lossy.x = 1f;
        if (Mathf.Abs(lossy.y) < 0.0001f) lossy.y = 1f;
        if (Mathf.Abs(lossy.z) < 0.0001f) lossy.z = 1f;

        CapsuleCollider cc = obstacle.AddComponent<CapsuleCollider>();
        cc.direction = 1; // Y axis

        float worldHeight = Mathf.Max(colliderMinHeight, bounds.size.y * colliderHeightFactor);
        float footprintWidth = Mathf.Max(bounds.size.x, bounds.size.z);
        float worldRadius = Mathf.Max(colliderMinRadius, footprintWidth * 0.5f * colliderRadiusFactor);

        // Put the capsule at the bottom of the bounds so only trunks/stumps block movement.
        Vector3 bottomWorld = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Vector3 centerWorld = bottomWorld + Vector3.up * (worldHeight * 0.5f);

        cc.center = obstacle.transform.InverseTransformPoint(centerWorld);
        cc.height = worldHeight / Mathf.Abs(lossy.y);
        cc.radius = worldRadius / Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
    }

    private int GetFirstLayerFromMask(LayerMask mask)
    {
        int value = mask.value;
        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0) return i;
        }
        return 0;
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // Editor visualization so you can see the arena footprint before pressing play.
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(arenaSize, 0.1f, arenaSize));

        Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(playerSpawnPoint, playerClearRadius);
    }
}
