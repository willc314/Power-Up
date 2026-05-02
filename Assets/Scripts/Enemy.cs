using UnityEngine;

/// <summary>
/// Single enemy controller used by all enemy prefabs.
/// Tweak the stats in the Inspector to make each prefab feel different,
/// and pick a Behavior to change how it moves and attacks.
///
/// Suggested presets:
///   * Chaser  — HP 30,  Damage 8,  Speed 3.5
///   * Charger — HP 25,  Damage 12, Speed 2.0  (dashes are fast)
///   * Tank    — HP 120, Damage 20, Speed 1.5
///   * Ranged  — HP 20,  Damage 6,  Speed 2.5  (assign Projectile Prefab)
///
/// This version keeps your partner's hit-particle effects and adds the systems
/// needed by your powerup/arena work:
///   * simple obstacle avoidance around structures, rocks, and trees
///   * powerup drops when enemies die
///
/// Setup checklist on the Enemy GameObject:
///   * Rigidbody  (Use Gravity = on; the script freezes X/Z rotation)
///   * Collider   (e.g. CapsuleCollider sized to the enemy)
///   * Tag        e.g. "Enemy"
///   * Layer      "Enemy"  (so weapon layer masks can find it)
///   * Power Up Prefab assigned if this enemy should drop powerups
///   * Obstacle Mask set to your Obstacle/environment layer for avoidance
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Enemy : MonoBehaviour
{
    public enum Behavior { Chaser, Charger, Tank, Ranged }

    [Header("Behavior")]
    public Behavior behavior = Behavior.Chaser;

    [Header("Stats")]
    public float maxHP = 30f;
    public float attackDamage = 8f;
    public float moveSpeed = 3.5f;

    [Header("Melee")]
    [Tooltip("Distance from the player at which a melee attack lands. Ignored by Ranged.")]
    public float attackRange = 1.4f;
    [Tooltip("Seconds between melee hits.")]
    public float attackCooldown = 1.0f;

    [Header("Detection")]
    [Tooltip("Maximum distance at which the enemy notices the player. 0 = always.")]
    public float aggroRange = 25f;

    [Header("Powerup Drop")]
    [Tooltip("PowerUp prefab that can drop when this enemy dies.")]
    public PowerUp powerUpPrefab;

    [Tooltip("Chance from 0 to 1 that this enemy drops a powerup on death.")]
    [Range(0f, 1f)] public float powerUpDropChance = 0.25f;

    [Tooltip("Weapon powerup types this enemy can drop. If empty, sword/shield/bow/dagger are used.")]
    public eWeaponType[] possiblePowerUpTypes = new eWeaponType[]
    {
        eWeaponType.sword,
        eWeaponType.shield,
        eWeaponType.bow,
        eWeaponType.dagger
    };

    [Tooltip("How high above the enemy position the powerup spawns.")]
    public float powerUpDropHeight = 0.6f;

    [Header("Obstacle Avoidance")]
    [Tooltip("If true, enemy tries to steer around obstacles instead of walking straight into them.")]
    public bool useObstacleAvoidance = true;

    [Tooltip("Layers considered obstacles. Usually set this to the Obstacle layer.")]
    public LayerMask obstacleMask = ~0;

    [Tooltip("How far ahead the enemy checks for obstacles.")]
    public float obstacleCheckDistance = 2.5f;

    [Tooltip("How far left/right the enemy checks when deciding which way to steer.")]
    public float sideCheckDistance = 2.0f;

    [Tooltip("Radius of the obstacle check sphere cast. Larger values make enemies avoid earlier.")]
    public float obstacleCheckRadius = 0.45f;

    [Tooltip("How strongly the enemy steers sideways when blocked.")]
    public float avoidanceStrength = 1.25f;

    [Header("Hit Particles")]
    [Tooltip("Number of debris particles spawned when this enemy takes damage. Set to 0 to disable.")]
    public int hitParticleCount = 8;
    [Tooltip("Color of the hit particles.")]
    public Color hitParticleColor = new Color(0.7f, 0.1f, 0.1f);
    [Tooltip("Initial speed of hit particles.")]
    public float hitParticleSpeed = 5f;
    [Tooltip("Size (edge length) of each particle cube.")]
    public float hitParticleSize = 0.18f;
    [Tooltip("Seconds before each particle self-destroys.")]
    public float hitParticleLifetime = 0.5f;
    [Tooltip("Spread cone angle in degrees. 0 = laser-straight; 90 = full hemisphere.")]
    public float hitParticleSpread = 35f;
    [Tooltip("Vertical offset above the enemy pivot where particles spawn.")]
    public float hitParticleHeight = 1.0f;

    [Header("Charger settings")]
    [Tooltip("How much faster than moveSpeed the dash is.")]
    public float dashSpeedMultiplier = 3f;
    [Tooltip("Seconds the enemy telegraphs (stands still) before dashing.")]
    public float dashTelegraphTime = 0.6f;
    [Tooltip("Seconds the dash itself lasts.")]
    public float dashDuration = 0.4f;
    [Tooltip("Seconds of recovery after a dash before starting another telegraph.")]
    public float dashRecovery = 1.2f;

    [Header("Ranged settings")]
    [Tooltip("Projectile prefab to fire. Must have an EnemyProjectile component.")]
    public EnemyProjectile projectilePrefab;
    [Tooltip("Optional spawn point for projectiles. If null, projectiles spawn at the enemy's position + small forward offset.")]
    public Transform projectileSpawn;
    [Tooltip("Preferred distance the ranged enemy keeps from the player.")]
    public float preferredRange = 8f;
    [Tooltip("How close the player can get before the ranged enemy retreats.")]
    public float retreatRange = 5f;
    [Tooltip("Seconds between shots.")]
    public float shootCooldown = 1.5f;

    // --- runtime state ---
    private float currentHP;
    private float meleeTimer;
    private float shootTimer;
    private Rigidbody rb;
    private Hero player;
    private DamageFlash damageFlash;

    // Charger state machine
    private enum ChargerPhase { Approach, Telegraph, Dash, Recover }
    private ChargerPhase chargerPhase = ChargerPhase.Approach;
    private float chargerPhaseTimer;
    private Vector3 dashDirection;

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public bool IsDead { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        damageFlash = GetComponent<DamageFlash>();
        currentHP = maxHP;
    }

    private void Update()
    {
        if (meleeTimer > 0f) meleeTimer -= Time.deltaTime;
        if (shootTimer > 0f) shootTimer -= Time.deltaTime;
    }

    private void FixedUpdate()
    {
        // Lazy lookup: works whether the hero was placed in the scene or spawned by ArenaGenerator after this enemy.
        if (player == null) player = Hero.Instance;

        if (IsDead || player == null || player.IsDead) { StopMoving(); return; }

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        if (aggroRange > 0f && dist > aggroRange)
        {
            StopMoving();
            return;
        }

        Vector3 dir = dist > 0.001f ? toPlayer / dist : Vector3.zero;
        // Always face the player.
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        switch (behavior)
        {
            case Behavior.Chaser:  TickChaser(dir, dist);  break;
            case Behavior.Tank:    TickTank(dir, dist);    break;
            case Behavior.Charger: TickCharger(dir, dist); break;
            case Behavior.Ranged:  TickRanged(dir, dist);  break;
        }
    }

    // ---------- Behaviors ----------

    private void TickChaser(Vector3 dir, float dist)
    {
        if (dist > attackRange)
            MoveInDirection(GetAvoidedDirection(dir), moveSpeed);
        else
        {
            StopMoving();
            TryMelee();
        }
    }

    private void TickTank(Vector3 dir, float dist)
    {
        if (dist > attackRange)
            MoveInDirection(GetAvoidedDirection(dir), moveSpeed);
        else
        {
            StopMoving();
            TryMelee();
        }
    }

    private void TickCharger(Vector3 dir, float dist)
    {
        chargerPhaseTimer -= Time.fixedDeltaTime;

        switch (chargerPhase)
        {
            case ChargerPhase.Approach:
                MoveInDirection(GetAvoidedDirection(dir), moveSpeed);
                // Once we're roughly in line of sight, telegraph a dash.
                if (dist < preferredRange || dist < attackRange + 3f)
                {
                    chargerPhase = ChargerPhase.Telegraph;
                    chargerPhaseTimer = dashTelegraphTime;
                    StopMoving();
                }
                break;

            case ChargerPhase.Telegraph:
                StopMoving();
                if (chargerPhaseTimer <= 0f)
                {
                    dashDirection = GetAvoidedDirection(dir);
                    chargerPhase = ChargerPhase.Dash;
                    chargerPhaseTimer = dashDuration;
                }
                break;

            case ChargerPhase.Dash:
                MoveInDirection(dashDirection, moveSpeed * dashSpeedMultiplier);
                if (dist <= attackRange) TryMelee();
                if (chargerPhaseTimer <= 0f)
                {
                    chargerPhase = ChargerPhase.Recover;
                    chargerPhaseTimer = dashRecovery;
                    StopMoving();
                }
                break;

            case ChargerPhase.Recover:
                StopMoving();
                if (chargerPhaseTimer <= 0f)
                {
                    chargerPhase = ChargerPhase.Approach;
                }
                break;
        }
    }

    private void TickRanged(Vector3 dir, float dist)
    {
        // Kite: keep around `preferredRange`. Back away if too close.
        if (dist < retreatRange)
            MoveInDirection(GetAvoidedDirection(-dir), moveSpeed);
        else if (dist > preferredRange)
            MoveInDirection(GetAvoidedDirection(dir), moveSpeed);
        else
            StopMoving();

        if (shootTimer <= 0f && projectilePrefab != null)
        {
            Shoot(dir);
            shootTimer = shootCooldown;
        }
    }

    // ---------- Helpers ----------

    private Vector3 GetAvoidedDirection(Vector3 desiredDirection)
    {
        desiredDirection.y = 0f;

        if (!useObstacleAvoidance)
            return desiredDirection.normalized;

        if (desiredDirection.sqrMagnitude < 0.001f)
            return Vector3.zero;

        desiredDirection.Normalize();

        Vector3 origin = transform.position + Vector3.up * 0.7f;

        bool forwardBlocked = Physics.SphereCast(
            origin,
            obstacleCheckRadius,
            desiredDirection,
            out RaycastHit forwardHit,
            obstacleCheckDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore
        );

        if (!forwardBlocked)
            return desiredDirection;

        Vector3 right = Vector3.Cross(Vector3.up, desiredDirection).normalized;
        Vector3 left = -right;

        bool rightBlocked = Physics.SphereCast(origin, obstacleCheckRadius, right, out RaycastHit rightHit, sideCheckDistance, obstacleMask, QueryTriggerInteraction.Ignore);
        bool leftBlocked = Physics.SphereCast(origin, obstacleCheckRadius, left, out RaycastHit leftHit, sideCheckDistance, obstacleMask, QueryTriggerInteraction.Ignore);

        Vector3 chosenSide;

        if (!rightBlocked && leftBlocked)
            chosenSide = right;
        else if (rightBlocked && !leftBlocked)
            chosenSide = left;
        else if (!rightBlocked && !leftBlocked)
        {
            float rightScore = player != null ? Vector3.Distance(transform.position + right, player.transform.position) : 0f;
            float leftScore = player != null ? Vector3.Distance(transform.position + left, player.transform.position) : 0f;
            chosenSide = rightScore < leftScore ? right : left;
        }
        else
            chosenSide = -desiredDirection;

        Vector3 avoided = desiredDirection + chosenSide * avoidanceStrength;
        avoided.y = 0f;

        if (avoided.sqrMagnitude < 0.001f)
            return chosenSide.normalized;

        return avoided.normalized;
    }

    private void MoveInDirection(Vector3 dir, float speed)
    {
        // Preserve gravity on Y; only drive XZ velocity.
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();

        Vector3 v = dir * speed;
        v.y = rb.velocity.y;
        rb.velocity = v;
    }

    private void StopMoving()
    {
        if (rb == null) return;
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    private void TryMelee()
    {
        if (meleeTimer > 0f || player == null) return;
        player.TakeDamage(attackDamage);
        meleeTimer = attackCooldown;
    }

    private void Shoot(Vector3 dir)
    {
        Vector3 spawnPos = projectileSpawn != null
            ? projectileSpawn.position
            : transform.position + transform.forward * 0.8f + Vector3.up * 1.0f;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        EnemyProjectile p = Instantiate(projectilePrefab, spawnPos, rot);
        p.Launch(dir, attackDamage);
    }

    // ---------- Damage / death ----------

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        currentHP = Mathf.Max(0f, currentHP - amount);
        if (damageFlash != null) damageFlash.Flash();
        SpawnHitParticles();
        if (currentHP <= 0f) Die();
    }

    private void SpawnHitParticles()
    {
        if (hitParticleCount <= 0) return;
        // Direction: from the player outward through the enemy. Falls back to enemy's facing if the player is missing.
        Vector3 dir;
        if (Hero.Instance != null)
        {
            dir = transform.position - Hero.Instance.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir.Normalize();
        }
        else
        {
            dir = transform.forward;
        }
        Vector3 origin = transform.position + Vector3.up * hitParticleHeight;
        HitParticles.EmitBurst(origin, dir,
            count: hitParticleCount,
            speed: hitParticleSpeed,
            lifetime: hitParticleLifetime,
            size: hitParticleSize,
            color: hitParticleColor,
            spreadAngle: hitParticleSpread,
            useGravity: true);
    }

    private void Die()
    {
        IsDead = true;
        StopMoving();
        TryDropPowerUp();
        Destroy(gameObject);
    }

    private void TryDropPowerUp()
    {
        if (powerUpPrefab == null) return;
        if (Random.value > powerUpDropChance) return;

        eWeaponType dropType = PickRandomPowerUpType();
        if (dropType == eWeaponType.none) return;

        Vector3 spawnPos = transform.position + Vector3.up * powerUpDropHeight;
        PowerUp powerUp = Instantiate(powerUpPrefab, spawnPos, Quaternion.identity);
        powerUp.SetType(dropType);
    }

    private eWeaponType PickRandomPowerUpType()
    {
        eWeaponType[] pool = possiblePowerUpTypes;

        if (pool == null || pool.Length == 0)
        {
            pool = new eWeaponType[]
            {
                eWeaponType.sword,
                eWeaponType.shield,
                eWeaponType.bow,
                eWeaponType.dagger
            };
        }

        for (int i = 0; i < 20; i++)
        {
            eWeaponType picked = pool[Random.Range(0, pool.Length)];
            if (picked != eWeaponType.none) return picked;
        }

        return eWeaponType.none;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (useObstacleAvoidance)
        {
            Gizmos.color = Color.cyan;
            Vector3 origin = transform.position + Vector3.up * 0.7f;
            Gizmos.DrawWireSphere(origin + transform.forward * obstacleCheckDistance, obstacleCheckRadius);
            Gizmos.DrawLine(origin, origin + transform.forward * obstacleCheckDistance);
        }

        if (behavior == Behavior.Ranged)
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, preferredRange);
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, retreatRange);
        }
    }
}
