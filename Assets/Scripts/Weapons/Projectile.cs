using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic straight-line projectile used by Crossbow (Arrow_Regular), Bow
/// (Arrow_Piercing), and Dagger (thrown). Travels at fixed speed in its
/// initial direction, damages enemies on contact, optionally pierces, and
/// optionally spins for visual flair.
/// </summary>
public class Projectile : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Travel speed in units/sec.")]
    public float speed = 20f;
    [Tooltip("Seconds before the projectile self-destroys if it doesn't hit anything.")]
    public float lifetime = 4f;
    [Tooltip("Max enemies the projectile passes through. 0 = stop on first hit; -1 = pierces forever.")]
    public int pierceCount = 0;

    [Header("Hit Volume")]
    [Tooltip("Thickness of the hit volume swept along the projectile's path each frame.")]
    public float hitRadius = 0.3f;
    [Tooltip("Vertical height of the hit volume. Use a tall value (1.5-2.5) so projectiles catch short enemies whose colliders sit below the projectile's flight height. If <= 2*hitRadius, it behaves as a pure sphere.")]
    public float hitHeight = 2.0f;

    [Header("Damage Falloff (optional)")]
    [Tooltip("Multiplier applied to the running damage scale after every successful hit. 1 = no falloff (every hit deals full damage). 0.5 = damage halves each hit. 0.25 = damage drops to a quarter each hit. Combined with damageFalloffFloor to keep later hits from going to zero.")]
    [Range(0f, 1f)] public float damageFalloffPerHit = 1f;
    [Tooltip("Minimum multiplier the damage scale can fall to. 0 = no floor (damage trends to zero). 0.25 = damage never drops below 25% of the original — useful for piercing/homing arrows that loop back so they keep doing meaningful (but reduced) damage on every hit.")]
    [Range(0f, 1f)] public float damageFalloffFloor = 0f;

    [Header("Visual")]
    [Tooltip("Spin around the projectile's local forward axis, deg/sec. 0 = no spin (arrows). Use 720+ for thrown daggers.")]
    public float spinSpeed = 0f;
    [Tooltip("Initial Euler offset applied so the model points along travel. Tweak if the arrow/dagger model points the wrong way (try (90,0,0), (0,90,0), or (0,0,90)).")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Homing (optional — used by fully-charged Bow shots)")]
    [Tooltip("If true, the projectile actively curves toward the nearest enemy each frame. Combine with a high homingTurnSpeed for near-perfect tracking.")]
    public bool homing = false;
    [Tooltip("Base degrees per second the flight direction can rotate toward the homing target at the moment of last hit. 360 = ~perfect aim — the arrow can do a full U-turn in one second. Effective rate ramps UP from this value while the arrow goes without hitting an enemy (see homingTurnSpeedRampPerSecond).")]
    public float homingTurnSpeed = 360f;
    [Tooltip("Degrees/sec ADDED to the effective turn rate per second the arrow goes without landing a hit. Prevents the arrow from spiraling around an enemy it can't quite catch — given enough time the turn rate gets tight enough to break the orbit. Reset to zero whenever the arrow lands a hit. 0 = disabled.")]
    public float homingTurnSpeedRampPerSecond = 360f;
    [Tooltip("Hard cap on the effective turn rate (deg/sec). Stops the ramp from growing unbounded against unreachable targets. 0 or negative = uncapped.")]
    public float homingTurnSpeedMax = 2160f;
    [Tooltip("Seconds the projectile actively homes for. After this, it falls back to a straight line.")]
    public float homingDuration = 4f;
    [Tooltip("Stops looking for / tracking targets further than this. Small radius = the projectile flies straight if no enemy is close.")]
    public float homingMaxRange = 30f;
    [Tooltip("If true, the homing target is re-acquired every frame so a dying enemy is replaced immediately. Very useful for piercing arrows that need to chain through a crowd.")]
    public bool homingRetargetEachFrame = true;

    private Vector3 direction;
    private float damage;
    private float damageScale; // running multiplier on `damage` for the next hit (multiplied by damageFalloffPerHit after each hit, floored at damageFalloffFloor)
    private LayerMask enemyLayers;
    private int pierceRemaining;
    private float homingElapsed;
    private float homingTimeSinceHit; // counts up while no hit lands; resets to 0 on each hit
    private Enemy homingTarget;
    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[16];

    // Cached reference to an optional MeteorArmer — added by the Hero's
    // Meteor general augment when a crit shot rolls successfully. On the
    // first valid enemy hit we call TryConsume(enemyPos) to spawn the
    // meteor at that enemy's position. Cached lazily on Launch since the
    // armer is added AFTER Launch by the firing weapon.
    private MeteorArmer meteorArmer;
    // Cached reference to an optional ShivArmer — added by DaggerWeapon
    // when the Elemental Shiv augment is active. Every enemy hit (not
    // just the first, unlike meteor) spawns its own clone fan against
    // that enemy. Cached lazily because the armer is attached AFTER Launch.
    private ShivArmer shivArmer;
    private bool shivArmerLookedUp;
    // Cached reference to an optional SkyBeamArmer — added by BowWeapon
    // when Heavenly Gale is active. Each enemy hit independently rolls
    // chancePerHit; on success spawns a sky death beam on that enemy.
    // Cached lazily because the armer is attached AFTER Launch.
    private SkyBeamArmer skyBeamArmer;
    private bool skyBeamArmerLookedUp;

    public void Launch(Vector3 dir, float damage, LayerMask enemyLayers)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        this.damage = damage;
        this.damageScale = 1f; // first hit always lands at full damage; falloff kicks in afterward
        this.enemyLayers = enemyLayers;
        this.pierceRemaining = pierceCount;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(modelRotationOffset);
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // Homing: rotate `direction` toward the nearest live enemy. Skips
        // already-hit enemies (for piercing shots) so the arrow chains
        // between targets instead of looping back into the same one.
        if (homing && homingElapsed < homingDuration)
        {
            homingElapsed += Time.deltaTime;
            homingTimeSinceHit += Time.deltaTime;
            if (homingTarget == null || homingTarget.IsDead || homingRetargetEachFrame)
                homingTarget = FindNearestEnemy();
            if (homingTarget != null && !homingTarget.IsDead)
            {
                Vector3 toTarget = homingTarget.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 desired = toTarget.normalized;
                    // Effective turn rate ramps up while we go without landing
                    // a hit — keeps the arrow from spiraling forever around a
                    // target it can't quite intercept. Cap prevents runaway
                    // values when the ramp persists for several seconds.
                    float effTurnSpeed = Mathf.Max(0f, homingTurnSpeed)
                        + Mathf.Max(0f, homingTurnSpeedRampPerSecond) * homingTimeSinceHit;
                    if (homingTurnSpeedMax > 0f)
                        effTurnSpeed = Mathf.Min(effTurnSpeed, homingTurnSpeedMax);
                    float maxRad = effTurnSpeed * Mathf.Deg2Rad * Time.deltaTime;
                    direction = Vector3.RotateTowards(direction, desired, maxRad, 0f).normalized;
                    transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(modelRotationOffset);
                }
            }
        }

        Vector3 step = direction * speed * Time.deltaTime;
        Vector3 nextPos = transform.position + step;

        // Vertical capsule for hit detection — covers from foot height to head height
        // so short enemies aren't missed by an arrow flying at chest level.
        float halfExtent = Mathf.Max(0f, hitHeight * 0.5f - hitRadius);
        Vector3 p1 = transform.position + Vector3.up * halfExtent;
        Vector3 p2 = transform.position - Vector3.up * halfExtent;

        if (Physics.CapsuleCast(p1, p2, hitRadius, direction, out RaycastHit hit, step.magnitude + 0.01f, enemyLayers, QueryTriggerInteraction.Collide))
        {
            Enemy e = hit.collider.GetComponentInParent<Enemy>();
            if (e != null && alreadyHit.Add(e))
            {
                // Damage scale lets the projectile deal less per chained hit
                // (set up via damageFalloffPerHit + damageFalloffFloor). The
                // CURRENT scale is consumed by this hit, then we step it
                // toward the floor for the next hit.
                e.TakeDamage(damage * damageScale);
                // Meteor general augment: if this projectile was armed at
                // fire time, spawn the meteor on this first valid hit.
                // TryConsume disarms internally so subsequent hits in a
                // piercing chain don't double-fire.
                if (meteorArmer == null) meteorArmer = GetComponent<MeteorArmer>();
                if (meteorArmer != null) meteorArmer.TryConsume(e.transform.position);
                // Elemental Shiv (dagger only): EVERY enemy hit spawns a
                // clone fan against that enemy. Unlike meteor, no disarm —
                // a piercing thrown dagger spawns clones for each enemy
                // it pierces, scaling the augment's value with pierce.
                if (!shivArmerLookedUp) { shivArmer = GetComponent<ShivArmer>(); shivArmerLookedUp = true; }
                if (shivArmer != null) shivArmer.TriggerOn(e);
                // Heavenly Gale sky beam (bow only): EVERY enemy hit
                // independently rolls chancePerHit. Lets a piercing
                // arrow chain multiple beams across a packed group.
                if (!skyBeamArmerLookedUp) { skyBeamArmer = GetComponent<SkyBeamArmer>(); skyBeamArmerLookedUp = true; }
                if (skyBeamArmer != null) skyBeamArmer.OnEnemyHit(e);
                if (damageFalloffPerHit < 1f - 0.0001f)
                {
                    damageScale = Mathf.Max(damageFalloffFloor, damageScale * damageFalloffPerHit);
                }
                // Homing: when this arrow makes contact, drop the current
                // homing lock so the next FindNearestEnemy call picks a
                // different target, AND reset the no-hit ramp so the next
                // target gets the gentle base turn rate again (the ramp
                // only escalates while the arrow is failing to land hits).
                if (homing)
                {
                    if (homingTarget == e) homingTarget = null;
                    homingTimeSinceHit = 0f;
                }
                if (pierceCount >= 0 && pierceRemaining-- <= 0)
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        transform.position = nextPos;

        if (spinSpeed != 0f) transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// Find the nearest live, not-yet-hit enemy within homingMaxRange.
    /// Returns null if nothing qualifies — the projectile will fly straight
    /// in that case.
    ///
    /// Uses EnemySpawner.AliveEnemies (the authoritative registry) when
    /// available, falling back to Physics.OverlapSphere otherwise. The
    /// registry path avoids the buffer-fills-with-non-enemy-colliders
    /// problem (a 16-slot OverlapSphere result can be saturated by cacti
    /// or terrain on the same layer mask before any actual enemy slot is
    /// returned, which is what was hiding homing targets).
    ///
    /// Loop-back behavior: if the first pass (excluding already-hit enemies)
    /// finds nothing, alreadyHit is cleared and a second pass runs. This
    /// lets a homing arrow re-target a previously-damaged enemy when there
    /// are no fresh ones nearby — useful for single-target / boss fights
    /// so the arrow loops back instead of flying off into nothing.
    /// </summary>
    private Enemy FindNearestEnemy()
    {
        Enemy best = FindNearestEnemyExcludingAlreadyHit();
        if (best == null && alreadyHit.Count > 0)
        {
            // No fresh targets — let the arrow re-engage previously-hit
            // enemies. Clearing alreadyHit also lets the CapsuleCast hit
            // logic deal damage on the next contact (Add returns true again).
            alreadyHit.Clear();
            best = FindNearestEnemyExcludingAlreadyHit();
        }
        return best;
    }

    private Enemy FindNearestEnemyExcludingAlreadyHit()
    {
        Enemy best = null;
        float bestSqr = float.MaxValue;
        float maxSqr = homingMaxRange * homingMaxRange;

        var spawner = EnemySpawner.Instance;
        if (spawner != null && spawner.AliveEnemies != null)
        {
            var list = spawner.AliveEnemies;
            for (int i = 0; i < list.Count; i++)
            {
                Enemy e = list[i];
                if (e == null || e.IsDead) continue;
                if (alreadyHit.Contains(e)) continue;
                Vector3 d = e.transform.position - transform.position;
                d.y = 0f; // horizontal distance only — arrows fly at chest height
                float sqr = d.sqrMagnitude;
                if (sqr > maxSqr) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = e; }
            }
            return best;
        }

        // Fallback: Physics.OverlapSphere (only used if the spawner singleton
        // hasn't initialized yet, e.g. during very first-frame edge cases).
        int n = Physics.OverlapSphereNonAlloc(
            transform.position, homingMaxRange, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            Enemy e = hitBuffer[i] != null ? hitBuffer[i].GetComponentInParent<Enemy>() : null;
            if (e == null || e.IsDead) continue;
            if (alreadyHit.Contains(e)) continue;
            float d = (e.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = e; }
        }
        return best;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
        float halfExtent = Mathf.Max(0f, hitHeight * 0.5f - hitRadius);
        Vector3 p1 = transform.position + Vector3.up * halfExtent;
        Vector3 p2 = transform.position - Vector3.up * halfExtent;
        Gizmos.DrawWireSphere(p1, hitRadius);
        Gizmos.DrawWireSphere(p2, hitRadius);
        Gizmos.DrawLine(p1 + Vector3.right * hitRadius, p2 + Vector3.right * hitRadius);
        Gizmos.DrawLine(p1 - Vector3.right * hitRadius, p2 - Vector3.right * hitRadius);
        Gizmos.DrawLine(p1 + Vector3.forward * hitRadius, p2 + Vector3.forward * hitRadius);
        Gizmos.DrawLine(p1 - Vector3.forward * hitRadius, p2 - Vector3.forward * hitRadius);
    }
}
