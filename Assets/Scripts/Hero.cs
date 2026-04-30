using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Top-down hero controller for a Vampire Survivors-style roguelike.
/// - WASD moves the hero on the XZ plane (world-space, camera-relative-ish).
/// - The hero faces the mouse cursor.
/// - Left click swings the sword (melee arc hit).
/// - Right click is reserved for a future secondary weapon.
///
/// Setup checklist on the Hero GameObject:
///   * Rigidbody  (Use Gravity = on, the script will freeze X/Z rotation)
///   * Collider   (e.g. CapsuleCollider sized to the hero)
///   * Tag        "Player" (optional, but enemies look for it)
///   * Layer      e.g. "Player"
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Hero : MonoBehaviour
{
    [Header("Stats")]
    [Tooltip("Maximum hit points.")]
    public float maxHP = 100f;
    [Tooltip("Damage dealt by a single sword swing.")]
    public float attackDamage = 25f;
    [Tooltip("Movement speed in units per second.")]
    public float moveSpeed = 5f;

    [Header("Attack")]
    [Tooltip("Reach of the sword in units (radius in front of the hero).")]
    public float attackRange = 1.8f;
    [Tooltip("Half-angle of the swing arc in degrees. 90 = full 180-degree arc in front.")]
    [Range(10f, 180f)]
    public float attackArcDegrees = 90f;
    [Tooltip("Seconds between sword swings.")]
    public float attackCooldown = 0.4f;
    [Tooltip("Layers that the sword can damage. Set this to your 'Enemy' layer in the Inspector.")]
    public LayerMask enemyLayers = ~0;

    [Header("Aim")]
    [Tooltip("Y-height of the imaginary ground plane the mouse aim is projected onto. Match the hero's feet/ground height.")]
    public float aimPlaneY = 0f;

    [Header("Debug / Read-only")]
    [SerializeField] private float currentHP;
    [SerializeField] private float attackTimer;

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public bool IsDead { get; private set; }

    // --- internals ---
    private Rigidbody rb;
    private Camera cam;
    private Vector3 moveInput;
    private readonly Collider[] hitBuffer = new Collider[32];

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        cam = Camera.main;
        currentHP = maxHP;
    }

    private void Update()
    {
        if (IsDead) return;

        // --- Input ---
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S
        moveInput = new Vector3(h, 0f, v).normalized;

        // --- Aim ---
        FaceMouse();

        // --- Attack ---
        if (attackTimer > 0f) attackTimer -= Time.deltaTime;

        if (Input.GetMouseButtonDown(0) && attackTimer <= 0f)
        {
            SwingSword();
            attackTimer = attackCooldown;
        }

        // Right-click reserved for a secondary weapon. Hook a second attack here later.
        // if (Input.GetMouseButtonDown(1)) { /* secondary weapon */ }
    }

    private void FixedUpdate()
    {
        if (IsDead) return;

        Vector3 target = rb.position + moveInput * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(target);
    }

    private void FaceMouse()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        // Project onto a horizontal plane at the hero's feet.
        Plane ground = new Plane(Vector3.up, new Vector3(0f, aimPlaneY, 0f));
        if (ground.Raycast(ray, out float dist))
        {
            Vector3 point = ray.GetPoint(dist);
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }
    }

    private void SwingSword()
    {
        // Find every collider within attack range, then keep only the ones inside the arc in front.
        int count = Physics.OverlapSphereNonAlloc(transform.position, attackRange, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
        HashSet<Enemy> alreadyHit = new HashSet<Enemy>();

        for (int i = 0; i < count; i++)
        {
            Collider c = hitBuffer[i];
            if (c == null) continue;

            Vector3 toTarget = c.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) continue;

            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            if (angle > attackArcDegrees) continue;

            Enemy enemy = c.GetComponentInParent<Enemy>();
            if (enemy != null && alreadyHit.Add(enemy))
            {
                enemy.TakeDamage(attackDamage);
            }
        }

        // TODO: trigger an "Attack" animation here when you wire up the Animator.
        // GetComponent<Animator>()?.SetTrigger("Attack");
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        currentHP = Mathf.Max(0f, currentHP - amount);
        if (currentHP <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }

    private void Die()
    {
        IsDead = true;
        moveInput = Vector3.zero;
        // TODO: play death animation, show game-over UI, etc.
        Debug.Log("Hero died.");
    }

    // Visualize the swing arc in the editor.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.red;
        Vector3 fwd = Application.isPlaying ? transform.forward : transform.forward;
        Quaternion left  = Quaternion.AngleAxis(-attackArcDegrees, Vector3.up);
        Quaternion right = Quaternion.AngleAxis( attackArcDegrees, Vector3.up);
        Gizmos.DrawRay(transform.position, left  * fwd * attackRange);
        Gizmos.DrawRay(transform.position, right * fwd * attackRange);
    }
}
