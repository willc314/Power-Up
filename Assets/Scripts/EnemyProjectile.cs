using UnityEngine;

/// <summary>
/// Simple straight-line projectile spawned by ranged enemies.
///
/// Setup checklist on the projectile prefab:
///   * Rigidbody  (Use Gravity = off, Is Kinematic = off)
///   * Collider   (Is Trigger = on)
///   * Tag/Layer  e.g. layer "EnemyProjectile" (so the player layer can collide if you want)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("Travel speed in units per second.")]
    public float speed = 12f;
    [Tooltip("Seconds before the projectile self-destroys if it doesn't hit anything.")]
    public float lifetime = 4f;

    [Header("Homing")]
    [Tooltip("If true, the projectile gradually curves toward the Hero while in flight (good for cactus/seeker enemies).")]
    public bool homing = false;
    [Tooltip("How fast the projectile can rotate toward the Hero, in degrees/sec. Lower = lazier curve. 90 lets it barely keep up with a sidestepping hero; 360 makes it lock on hard.")]
    public float turnSpeed = 120f;
    [Tooltip("Seconds the projectile homes for before falling back to a straight line. Caps how long it can chase. 0 = no homing even if 'Homing' is true.")]
    public float homingDuration = 3f;
    [Tooltip("Stops homing if the Hero is further than this. Lets the projectile fly past instead of chasing forever.")]
    public float homingMaxRange = 30f;
    [Tooltip("Vertical offset above the Hero pivot the projectile aims at (chest = 0.5 for a typical character).")]
    public float homingAimHeight = 0.5f;

    [Tooltip("Brief grace period after spawn during which environment collisions don't destroy the projectile. Keeps weirdly-sized colliders from killing the projectile on frame 0.")]
    public float spawnSafetyTime = 0.05f;

    [Header("Visual Override")]
    [Tooltip("Optional visual prefab to spawn as a child on Awake. Use this to swap in a particle-effect VFX prefab (e.g. Gabriel Aguiar's vfx_Projectile_01, FireIce's FX_BulletTrail_Red) without rebuilding the projectile prefab from scratch. The original prefab still owns the Rigidbody/Collider/EnemyProjectile gameplay components — this just plays cosmetics on top.")]
    public GameObject visualPrefab;

    [Tooltip("Local Euler rotation applied to the spawned visual. Some VFX prefabs face +Z, some +Y; use this if the trail/sprite ends up sideways relative to the flight direction.")]
    public Vector3 visualLocalRotation = Vector3.zero;

    [Tooltip("Uniform local scale applied to the spawned visual. Useful when the source VFX was authored at a different size than this projectile.")]
    public float visualLocalScale = 1f;

    [Tooltip("If true, disables MeshRenderer / SkinnedMeshRenderer / SpriteRenderer components on the projectile root and existing children when a Visual Prefab is assigned. Lets a duplicated prefab replace its built-in arrow mesh with the new VFX without manually deleting the old visual nodes.")]
    public bool hideExistingVisualsWhenOverridden = true;

    private float damage;
    private Rigidbody rb;
    private float homingTimer;
    private float spawnTime;

    /// <summary>
    /// Static one-shot suppression. When true, the next EnemyProjectile to run
    /// Awake will SKIP its visualPrefab spawn (and consume the flag back to
    /// false). Use the <see cref="InstantiateNoVisual"/> helper to set + spawn
    /// + auto-reset atomically — that's the safe API. Manual writes are only
    /// supported for callers that genuinely need to gate a single Instantiate.
    ///
    /// Why static-and-consumed: visualPrefab is spawned in Awake, which runs
    /// during Instantiate (before the caller can flip a per-instance flag). A
    /// single static toggle, set immediately before Instantiate, lets the
    /// caller suppress without needing the projectile to know who spawned it.
    /// </summary>
    public static bool SuppressNextVisualOverride;

    /// <summary>
    /// Helper for "spawn this projectile WITHOUT its configured visualPrefab".
    /// SlimeGod uses this on respawns where the FX trails would otherwise
    /// cause heavy lag at high projectile counts.
    /// </summary>
    public static T InstantiateNoVisual<T>(T prefab, Vector3 position, Quaternion rotation) where T : EnemyProjectile
    {
        SuppressNextVisualOverride = true;
        T inst = Instantiate(prefab, position, rotation);
        // Defensive: in case Awake didn't run (shouldn't happen but bail-out
        // for some Unity-internal reason), don't leave the flag dangling for
        // an unrelated downstream Instantiate to consume.
        SuppressNextVisualOverride = false;
        return inst;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Make sure the projectile's collider is a trigger so OnTriggerEnter fires.
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;

        if (SuppressNextVisualOverride)
        {
            SuppressNextVisualOverride = false; // consume — single-shot flag
            // Skip the visualPrefab instantiate. The original prefab's built-in
            // renderers stay active because hideExistingVisualsWhenOverridden
            // wasn't applied (we never entered SpawnVisualOverride).
        }
        else
        {
            SpawnVisualOverride();
        }
    }

    /// <summary>
    /// If a visual prefab is configured, instantiate it as a child of this
    /// projectile and (optionally) hide the prefab's built-in visuals so the
    /// override reads cleanly. Cosmetic-only — doesn't touch the gameplay
    /// rigidbody/collider/damage path.
    /// </summary>
    private void SpawnVisualOverride()
    {
        if (visualPrefab == null) return;

        if (hideExistingVisualsWhenOverridden)
        {
            // Disable any pre-existing renderers on the prefab so they don't
            // visually fight with the new VFX. Use enabled=false instead of
            // destroying so the user can flip hideExistingVisualsWhenOverridden
            // off and recover the old look without rebuilding the prefab.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                // Skip renderers that were spawned by this method — they're
                // inside the visualPrefab subtree we instantiate below, and
                // we instantiate AFTER this loop so they don't exist yet.
                r.enabled = false;
            }
        }

        GameObject vfx = Instantiate(visualPrefab, transform);
        vfx.transform.localPosition = Vector3.zero;
        vfx.transform.localRotation = Quaternion.Euler(visualLocalRotation);
        vfx.transform.localScale    = Vector3.one * Mathf.Max(0.0001f, visualLocalScale);
    }

    public void Launch(Vector3 direction, float damageAmount)
    {
        damage = damageAmount;
        // Direction is full 3D so the projectile can fly at an angle (e.g. a tall
        // slime firing down at the player's chest). Older callers that pass a
        // horizontal vector (y already 0) are unaffected.
        if (direction.sqrMagnitude > 0.0001f) direction.Normalize();
        rb.velocity = direction * speed;
        homingTimer = 0f;
        spawnTime = Time.time;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        // Homing: gradually steer the velocity toward the Hero.
        if (!homing || homingDuration <= 0f) return;

        homingTimer += Time.fixedDeltaTime;
        if (homingTimer >= homingDuration) return;

        Hero player = Hero.Instance;
        if (player == null || player.IsDead) return;

        Vector3 target = player.transform.position + Vector3.up * homingAimHeight;
        Vector3 toTarget = target - transform.position;
        if (toTarget.sqrMagnitude > homingMaxRange * homingMaxRange) return;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Vector3 currentDir = rb.velocity.sqrMagnitude > 0.0001f ? rb.velocity.normalized : transform.forward;
        Vector3 desiredDir = toTarget.normalized;

        float maxRadians = turnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime;
        Vector3 newDir = Vector3.RotateTowards(currentDir, desiredDir, maxRadians, 0f);

        rb.velocity = newDir * speed;
        transform.rotation = Quaternion.LookRotation(newDir, Vector3.up);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Don't hit other enemies or other enemy projectiles.
        if (other.GetComponentInParent<Enemy>() != null) return;
        if (other.GetComponent<EnemyProjectile>() != null) return;

        Hero hero = other.GetComponentInParent<Hero>();
        if (hero != null)
        {
            hero.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        // Hit a wall or environment — disappear.
        // Skip during the spawn safety window so a weirdly-shaped collider doesn't
        // destroy the projectile by overlapping the ground/an obstacle on frame 0.
        if (Time.time - spawnTime < spawnSafetyTime) return;
        Destroy(gameObject);
    }
}
