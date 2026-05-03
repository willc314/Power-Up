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
///   * powerup drops when enemies die, including sword, shield, bow, dagger, crossbow, and grenade
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
    public enum Behavior { Chaser, Charger, Tank, Ranged, Crossbow, SlimeKing }

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

    [Header("Debug")]
    [Tooltip("If true, shows current HP as a small label above the enemy in both Scene and Game view. Useful for tuning weapon damage.")]
    public bool debugShowHealth = true;
    [Tooltip("How far above the enemy's pivot the HP label sits.")]
    public float debugLabelHeight = 2.5f;

    [Header("Powerup Drop")]
    [Tooltip("PowerUp prefab that can drop when this enemy dies.")]
    public PowerUp powerUpPrefab;

    [Tooltip("Chance from 0 to 1 that this enemy drops a powerup on death.")]
    [Range(0f, 1f)] public float powerUpDropChance = 0.25f;

    [Tooltip("If true, enemies can drop every weapon powerup, ignoring the Inspector list.")]
    public bool dropAllWeaponPowerUps = true;

    [Tooltip("Weapon powerup types this enemy can drop. Used only if Drop All Weapon Powerups is false.")]
    public eWeaponType[] possiblePowerUpTypes = new eWeaponType[]
    {
        eWeaponType.sword,
        eWeaponType.shield,
        eWeaponType.bow,
        eWeaponType.dagger,
        eWeaponType.crossbow,
        eWeaponType.grenade
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

    [Header("Crossbow / Telegraph settings (Behavior=Crossbow)")]
    [Tooltip("Projectile spawned when the telegraph completes. Use an EnemyProjectile prefab (e.g. Arrow_Regular with EnemyProjectile attached).")]
    public EnemyProjectile crossbowProjectile;
    [Tooltip("Damage of each crossbow shot (separate from melee damage).")]
    public float crossbowDamage = 25f;
    [Tooltip("Override the projectile's Speed. 0 = use the prefab default.")]
    public float crossbowProjectileSpeed = 18f;
    [Tooltip("Vertical offset above the enemy's pivot where the arrow visually originates and the telegraph line starts. For a tall slime, set this to its 'mouth' height.")]
    public float crossbowSpawnHeight = 1.5f;
    [Tooltip("Vertical offset above the player's pivot that the telegraph and arrow aim at. Use ~0.5 for a typical character (chest), 0.0 for feet, 1.0 for head.")]
    public float crossbowTargetHeight = 0.5f;
    [Tooltip("Seconds the enemy spends telegraphing before firing.")]
    public float telegraphDuration = 2f;
    [Tooltip("How fast the telegraph line tracks the player's current position, in units/sec. Lower = harder to dodge if you're slow, easier if you sidestep. 0 = locks on the player's position at the start of the telegraph.")]
    public float telegraphTrackSpeed = 3f;
    [Tooltip("Color of the telegraph line.")]
    public Color telegraphColor = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Width of the telegraph line in world units.")]
    public float telegraphLineWidth = 0.08f;
    [Tooltip("How far past the player the telegraph line continues drawing (purely visual, hint of where the arrow keeps going).")]
    public float telegraphExtensionDistance = 6f;
    [Tooltip("Maximum distance at which the enemy will start telegraphing a shot.")]
    public float crossbowAimMaxRange = 25f;
    [Tooltip("Seconds between consecutive crossbow shots.")]
    public float crossbowAttackCooldown = 4f;

    [Header("Slime King (Behavior=SlimeKing)")]
    [Tooltip("Seconds spent in Ranged phase before jumping to melee.")]
    public float skRangedDuration = 12f;
    [Tooltip("Seconds spent in Melee phase before jumping back to ranged.")]
    public float skMeleeDuration = 8f;
    [Tooltip("How long each jump (to melee or to ranged) takes from launch to landing.")]
    public float skJumpDuration = 1.0f;
    [Tooltip("Peak height of the jump arc, in units. 'Really high' = 8+.")]
    public float skJumpArcHeight = 8f;
    [Tooltip("How far the slime king jumps backward when transitioning melee→ranged.")]
    public float skRetreatJumpDistance = 12f;

    [Header("Slime King — Melee Mode")]
    [Tooltip("Move speed during the chase (before any boost).")]
    public float skMeleeSpeed = 4f;
    [Tooltip("Distance from the player at which a melee touch hits.")]
    public float skMeleeRange = 1.6f;
    [Tooltip("Damage per melee hit.")]
    public float skMeleeDamage = 18f;
    [Tooltip("Seconds between consecutive melee hits.")]
    public float skMeleeCooldown = 0.9f;
    [Tooltip("Speed multiplier active for skSpeedBoostDuration seconds after landing in melee mode.")]
    public float skSpeedBoostMultiplier = 1.8f;
    [Tooltip("Seconds the post-landing speed boost lasts.")]
    public float skSpeedBoostDuration = 4f;
    [Tooltip("Number of damage instances the shield absorbs after landing in melee mode.")]
    public int skShieldHits = 2;

    [Header("Slime King — Ranged Mode")]
    [Tooltip("Multiplier applied to telegraph duration AND attack cooldown after landing in ranged mode (smaller = faster). 0.5 = twice as fast.")]
    [Range(0.1f, 1f)] public float skAttackSpeedBoostMultiplier = 0.5f;
    [Tooltip("Seconds the post-landing attack speed boost lasts.")]
    public float skAttackSpeedBoostDuration = 6f;
    [Tooltip("If the player is closer than this during ranged phase, the slime king kites backward.")]
    public float skKiteRange = 7f;

    [Header("Split on Death")]
    [Tooltip("If true, spawns child enemies when this one dies (e.g. big slime → small slimes).")]
    public bool splitOnDeath = false;
    [Tooltip("Prefab to spawn when killed. Usually a smaller Enemy prefab.")]
    public GameObject splitPrefab;
    [Tooltip("Number of children to spawn.")]
    public int splitCount = 5;
    [Tooltip("How far from the dying enemy each child spawns. The children fan out in a ring.")]
    public float splitRadius = 1.6f;

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

    private float currentHP;
    private float meleeTimer;
    private float shootTimer;
    private Rigidbody rb;
    private Hero player;
    private DamageFlash damageFlash;

    private bool aiming;
    private float telegraphTimer;
    private Vector3 telegraphTargetPos;
    private float crossbowAttackTimer;
    private LineRenderer telegraphLine;

    private enum SlimeKingPhase { Ranged, JumpToMelee, Melee, JumpToRanged }
    private SlimeKingPhase skPhase = SlimeKingPhase.Ranged;
    private float skPhaseTimer;
    private Vector3 skJumpStart, skJumpEnd;
    private float skJumpProgress;
    private float skSpeedBoostTimer;
    private float skAttackSpeedBoostTimer;
    private int skCurrentShieldHits;
    private GameObject skShieldVisual;

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

        if (behavior == Behavior.Crossbow || behavior == Behavior.SlimeKing)
        {
            telegraphLine = gameObject.AddComponent<LineRenderer>();
            telegraphLine.startWidth = telegraphLineWidth;
            telegraphLine.endWidth = telegraphLineWidth;
            telegraphLine.material = GetTelegraphMaterial();
            telegraphLine.startColor = telegraphColor;
            telegraphLine.endColor = telegraphColor;
            telegraphLine.useWorldSpace = true;
            telegraphLine.positionCount = 2;
            telegraphLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            telegraphLine.receiveShadows = false;
            telegraphLine.enabled = false;
        }
    }

    private static Material cachedTelegraphMat;
    private static Material GetTelegraphMaterial()
    {
        if (cachedTelegraphMat != null) return cachedTelegraphMat;
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        cachedTelegraphMat = new Material(s);
        cachedTelegraphMat.hideFlags = HideFlags.HideAndDontSave;
        return cachedTelegraphMat;
    }

    private void Update()
    {
        if (meleeTimer > 0f) meleeTimer -= Time.deltaTime;
        if (shootTimer > 0f) shootTimer -= Time.deltaTime;
        if (crossbowAttackTimer > 0f) crossbowAttackTimer -= Time.deltaTime;
        if (skSpeedBoostTimer > 0f) skSpeedBoostTimer -= Time.deltaTime;
        if (skAttackSpeedBoostTimer > 0f) skAttackSpeedBoostTimer -= Time.deltaTime;
    }

    private void FixedUpdate()
    {
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
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        switch (behavior)
        {
            case Behavior.Chaser: TickChaser(dir, dist); break;
            case Behavior.Tank: TickTank(dir, dist); break;
            case Behavior.Charger: TickCharger(dir, dist); break;
            case Behavior.Ranged: TickRanged(dir, dist); break;
            case Behavior.Crossbow: TickCrossbow(dir, dist); break;
            case Behavior.SlimeKing: TickSlimeKing(dir, dist); break;
        }
    }

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

    private void TickCrossbow(Vector3 dir, float dist)
    {
        if (!aiming)
        {
            if (dist > crossbowAimMaxRange * 0.85f) MoveInDirection(dir, moveSpeed);
            else rb.velocity = new Vector3(0f, rb.velocity.y, 0f);

            if (crossbowAttackTimer <= 0f && dist <= crossbowAimMaxRange) StartAiming();
        }
        else
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            UpdateTelegraph();

            telegraphTimer -= Time.fixedDeltaTime;
            if (telegraphTimer <= 0f)
            {
                FireCrossbow();
                StopAiming();
            }
        }
    }

    private void StartAiming()
    {
        if (player == null) return;
        aiming = true;
        telegraphTimer = telegraphDuration * SkAttackMultiplier;
        telegraphTargetPos = player.transform.position + Vector3.up * crossbowTargetHeight;
        if (telegraphLine != null)
        {
            telegraphLine.startColor = telegraphColor;
            telegraphLine.endColor = telegraphColor;
            telegraphLine.startWidth = telegraphLineWidth;
            telegraphLine.endWidth = telegraphLineWidth;
            telegraphLine.enabled = true;
            UpdateTelegraph();
        }
    }

    private void StopAiming()
    {
        aiming = false;
        crossbowAttackTimer = crossbowAttackCooldown * SkAttackMultiplier;
        if (telegraphLine != null) telegraphLine.enabled = false;
    }

    private void UpdateTelegraph()
    {
        if (player == null) return;
        Vector3 playerAimPoint = player.transform.position + Vector3.up * crossbowTargetHeight;
        telegraphTargetPos = Vector3.MoveTowards(telegraphTargetPos, playerAimPoint, telegraphTrackSpeed * Time.fixedDeltaTime);

        if (telegraphLine != null)
        {
            Vector3 start = transform.position + Vector3.up * crossbowSpawnHeight;
            Vector3 end = telegraphTargetPos;
            if (telegraphExtensionDistance > 0f)
            {
                Vector3 lineDir = end - start;
                if (lineDir.sqrMagnitude > 0.0001f)
                    end += lineDir.normalized * telegraphExtensionDistance;
            }
            telegraphLine.SetPosition(0, start);
            telegraphLine.SetPosition(1, end);
        }
    }

    private void FireCrossbow()
    {
        if (crossbowProjectile == null) return;
        Vector3 start = transform.position + Vector3.up * crossbowSpawnHeight;
        Vector3 dir = telegraphTargetPos - start;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        EnemyProjectile p = Instantiate(crossbowProjectile, start, rot);
        if (crossbowProjectileSpeed > 0f) p.speed = crossbowProjectileSpeed;
        p.Launch(dir, crossbowDamage);
    }

    private void TickSlimeKing(Vector3 dir, float dist)
    {
        switch (skPhase)
        {
            case SlimeKingPhase.Ranged: TickSlimeRanged(dir, dist); break;
            case SlimeKingPhase.JumpToMelee: TickJump(SlimeKingPhase.Melee); break;
            case SlimeKingPhase.Melee: TickSlimeMelee(dir, dist); break;
            case SlimeKingPhase.JumpToRanged: TickJump(SlimeKingPhase.Ranged); break;
        }
    }

    private void TickSlimeRanged(Vector3 dir, float dist)
    {
        skPhaseTimer += Time.fixedDeltaTime;

        if (!aiming)
        {
            if (dist < skKiteRange) MoveInDirection(-dir, moveSpeed);
            else rb.velocity = new Vector3(0f, rb.velocity.y, 0f);

            if (crossbowAttackTimer <= 0f && dist <= crossbowAimMaxRange) StartAiming();
        }
        else
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            UpdateTelegraph();
            telegraphTimer -= Time.fixedDeltaTime;
            if (telegraphTimer <= 0f)
            {
                FireCrossbow();
                StopAiming();
            }
        }

        if (skPhaseTimer >= skRangedDuration && !aiming)
            StartJumpToMelee();
    }

    private void TickSlimeMelee(Vector3 dir, float dist)
    {
        skPhaseTimer += Time.fixedDeltaTime;

        float speed = moveSpeed * skMeleeSpeed / Mathf.Max(0.01f, moveSpeed) * (skSpeedBoostTimer > 0f ? skSpeedBoostMultiplier : 1f);
        speed = skMeleeSpeed * (skSpeedBoostTimer > 0f ? skSpeedBoostMultiplier : 1f);

        if (dist > skMeleeRange)
        {
            MoveInDirection(dir, speed);
        }
        else
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            if (meleeTimer <= 0f && player != null)
            {
                player.TakeDamage(skMeleeDamage);
                meleeTimer = skMeleeCooldown;
            }
        }

        if (skPhaseTimer >= skMeleeDuration)
            StartJumpToRanged();
    }

    private void TickJump(SlimeKingPhase nextPhase)
    {
        skJumpProgress += Time.fixedDeltaTime / Mathf.Max(0.05f, skJumpDuration);
        if (skJumpProgress >= 1f)
        {
            transform.position = skJumpEnd;
            rb.velocity = Vector3.zero;
            OnJumpLand(nextPhase);
            return;
        }

        Vector3 pos = Vector3.Lerp(skJumpStart, skJumpEnd, skJumpProgress);
        pos.y += 4f * skJumpArcHeight * skJumpProgress * (1f - skJumpProgress);
        transform.position = pos;
        rb.velocity = Vector3.zero;
    }

    private void StartJumpToMelee()
    {
        if (player == null) return;
        if (aiming) StopAiming();
        skPhase = SlimeKingPhase.JumpToMelee;
        skPhaseTimer = 0f;
        skJumpStart = transform.position;
        skJumpEnd = new Vector3(player.transform.position.x, transform.position.y, player.transform.position.z);
        skJumpProgress = 0f;
        rb.velocity = Vector3.zero;
    }

    private void StartJumpToRanged()
    {
        if (player == null) return;
        skPhase = SlimeKingPhase.JumpToRanged;
        skPhaseTimer = 0f;
        skJumpStart = transform.position;

        Vector3 awayFromPlayer = transform.position - player.transform.position;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude < 0.0001f) awayFromPlayer = -transform.forward;
        awayFromPlayer.Normalize();

        skJumpEnd = transform.position + awayFromPlayer * skRetreatJumpDistance;
        skJumpEnd.y = transform.position.y;
        skJumpProgress = 0f;
        rb.velocity = Vector3.zero;
    }

    private void OnJumpLand(SlimeKingPhase nextPhase)
    {
        skPhase = nextPhase;
        skPhaseTimer = 0f;

        if (nextPhase == SlimeKingPhase.Melee)
        {
            skSpeedBoostTimer = skSpeedBoostDuration;
            skCurrentShieldHits = skShieldHits;
            CreateShieldVisual();
        }
        else if (nextPhase == SlimeKingPhase.Ranged)
        {
            skAttackSpeedBoostTimer = skAttackSpeedBoostDuration;
            DestroyShieldVisual();
        }

        HitParticles.EmitBurst(transform.position + Vector3.up * 0.1f, Vector3.up,
            count: 18, speed: 6f, lifetime: 0.55f, size: 0.18f,
            color: hitParticleColor, spreadAngle: 75f, useGravity: true);
    }

    private void CreateShieldVisual()
    {
        DestroyShieldVisual();
        skShieldVisual = new GameObject("ShieldGlow");
        skShieldVisual.transform.SetParent(transform, false);
        skShieldVisual.transform.localPosition = Vector3.up * 1.0f;
        var l = skShieldVisual.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.4f, 0.7f, 1f);
        l.intensity = 5f;
        l.range = 4f;
        l.shadows = LightShadows.None;
    }

    private void DestroyShieldVisual()
    {
        if (skShieldVisual != null) Destroy(skShieldVisual);
        skShieldVisual = null;
    }

    private float SkAttackMultiplier => skAttackSpeedBoostTimer > 0f ? skAttackSpeedBoostMultiplier : 1f;

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

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        if (skCurrentShieldHits > 0)
        {
            skCurrentShieldHits--;
            HitParticles.EmitBurst(transform.position + Vector3.up, Vector3.up,
                count: 10, speed: 5f, lifetime: 0.4f, size: 0.12f,
                color: new Color(0.4f, 0.8f, 1f), spreadAngle: 60f, useGravity: false);
            if (skCurrentShieldHits <= 0) DestroyShieldVisual();
            return;
        }

        currentHP = Mathf.Max(0f, currentHP - amount);
        if (damageFlash != null) damageFlash.Flash();
        SpawnHitParticles();
        if (currentHP <= 0f) Die();
    }

    private void SpawnHitParticles()
    {
        if (hitParticleCount <= 0) return;

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
        if (telegraphLine != null) telegraphLine.enabled = false;

        if (GameManager.Instance != null) GameManager.Instance.OnEnemyKilled(this);

        if (splitOnDeath && splitPrefab != null && splitCount > 0)
        {
            for (int i = 0; i < splitCount; i++)
            {
                float angle = (360f / splitCount) * i + Random.Range(-15f, 15f);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * splitRadius;
                Vector3 pos = transform.position + offset + Vector3.up * 0.1f;
                Quaternion rot = Quaternion.Euler(0f, angle, 0f);
                Instantiate(splitPrefab, pos, rot);
            }
        }

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
        if (dropAllWeaponPowerUps)
        {
            eWeaponType[] allTypes = new eWeaponType[]
            {
                eWeaponType.sword,
                eWeaponType.shield,
                eWeaponType.bow,
                eWeaponType.dagger,
                eWeaponType.crossbow,
                eWeaponType.grenade
            };

            return allTypes[Random.Range(0, allTypes.Length)];
        }

        eWeaponType[] pool = possiblePowerUpTypes;

        if (pool == null || pool.Length == 0)
        {
            pool = new eWeaponType[]
            {
                eWeaponType.sword,
                eWeaponType.shield,
                eWeaponType.bow,
                eWeaponType.dagger,
                eWeaponType.crossbow,
                eWeaponType.grenade
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

    private void OnGUI()
    {
        if (!debugShowHealth || IsDead) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 worldPos = transform.position + Vector3.up * debugLabelHeight;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0f) return;

        string text = $"{currentHP:F0} / {maxHP:F0}";
        GUIStyle style = GUI.skin.label;
        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect r = new Rect(
            screenPos.x - size.x * 0.5f - 4f,
            Screen.height - screenPos.y - size.y - 2f,
            size.x + 8f,
            size.y + 4f);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = currentHP <= maxHP * 0.33f ? new Color(1f, 0.4f, 0.4f) : Color.white;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width, r.height), text);
        GUI.color = prev;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugShowHealth || !Application.isPlaying || IsDead) return;
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * debugLabelHeight,
            $"HP: {currentHP:F0}/{maxHP:F0}");
    }
#endif
}