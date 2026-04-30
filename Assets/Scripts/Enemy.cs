using UnityEngine;

/// <summary>
/// Single enemy controller used by all 4 enemy prefabs.
/// Tweak the stats in the Inspector to make each prefab feel different,
/// and pick a Behavior to change how it moves and attacks.
///
/// Suggested presets (just a starting point):
///   * Chaser  — HP 30,  Damage 8,  Speed 3.5
///   * Charger — HP 25,  Damage 12, Speed 2.0  (dashes are fast)
///   * Tank    — HP 120, Damage 20, Speed 1.5
///   * Ranged  — HP 20,  Damage 6,  Speed 2.5  (assign Projectile Prefab)
///
/// Setup checklist on the Enemy GameObject:
///   * Rigidbody  (Use Gravity = on; the script freezes X/Z rotation)
///   * Collider   (e.g. CapsuleCollider sized to the enemy)
///   * Tag        e.g. "Enemy"
///   * Layer      "Enemy"  (so the hero's sword layer mask can find it)
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

        currentHP = maxHP;
    }

    private void Start()
    {
        // Find the hero in the scene. For a tiny game this is fine; for bigger games cache it via a singleton or spawner.
        player = FindObjectOfType<Hero>();
    }

    private void Update()
    {
        if (meleeTimer > 0f) meleeTimer -= Time.deltaTime;
        if (shootTimer > 0f) shootTimer -= Time.deltaTime;
    }

    private void FixedUpdate()
    {
        if (IsDead || player == null || player.IsDead) { rb.velocity = Vector3.zero; return; }

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        if (aggroRange > 0f && dist > aggroRange)
        {
            rb.velocity = Vector3.zero;
            return;
        }

        Vector3 dir = dist > 0.001f ? toPlayer / dist : Vector3.zero;
        // Always face the player.
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        switch (behavior)
        {
            case Behavior.Chaser: TickChaser(dir, dist); break;
            case Behavior.Tank:   TickTank(dir, dist);   break;
            case Behavior.Charger:TickCharger(dir, dist);break;
            case Behavior.Ranged: TickRanged(dir, dist); break;
        }
    }

    // ---------- Behaviors ----------

    private void TickChaser(Vector3 dir, float dist)
    {
        if (dist > attackRange)
        {
            MoveInDirection(dir, moveSpeed);
        }
        else
        {
            rb.velocity = Vector3.zero;
            TryMelee();
        }
    }

    // Same idea as Chaser, but Tanks are slow & hard-hitting. Kept as its own method
    // so you can give it unique flavor later (e.g., a slam attack with windup).
    private void TickTank(Vector3 dir, float dist)
    {
        if (dist > attackRange)
        {
            MoveInDirection(dir, moveSpeed);
        }
        else
        {
            rb.velocity = Vector3.zero;
            TryMelee();
        }
    }

    private void TickCharger(Vector3 dir, float dist)
    {
        chargerPhaseTimer -= Time.fixedDeltaTime;

        switch (chargerPhase)
        {
            case ChargerPhase.Approach:
                MoveInDirection(dir, moveSpeed);
                // Once we're roughly in line of sight, telegraph a dash.
                if (dist < preferredRange || dist < attackRange + 3f)
                {
                    chargerPhase = ChargerPhase.Telegraph;
                    chargerPhaseTimer = dashTelegraphTime;
                    rb.velocity = Vector3.zero;
                }
                break;

            case ChargerPhase.Telegraph:
                rb.velocity = Vector3.zero;
                if (chargerPhaseTimer <= 0f)
                {
                    dashDirection = dir;
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
                    rb.velocity = Vector3.zero;
                }
                break;

            case ChargerPhase.Recover:
                rb.velocity = Vector3.zero;
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
        {
            MoveInDirection(-dir, moveSpeed);
        }
        else if (dist > preferredRange)
        {
            MoveInDirection(dir, moveSpeed);
        }
        else
        {
            rb.velocity = Vector3.zero;
        }

        if (shootTimer <= 0f && projectilePrefab != null)
        {
            Shoot(dir);
            shootTimer = shootCooldown;
        }
    }

    // ---------- Helpers ----------

    private void MoveInDirection(Vector3 dir, float speed)
    {
        // Preserve gravity on Y; only drive XZ velocity.
        Vector3 v = dir * speed;
        v.y = rb.velocity.y;
        rb.velocity = v;
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
        if (currentHP <= 0f) Die();
    }

    private void Die()
    {
        IsDead = true;
        rb.velocity = Vector3.zero;
        // TODO: play death animation, drop XP/loot, etc.
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        if (behavior == Behavior.Ranged)
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, preferredRange);
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, retreatRange);
        }
    }
}
