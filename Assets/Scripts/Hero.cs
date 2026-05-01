using UnityEngine;

/// <summary>
/// Top-down hero controller for a Vampire Survivors-style roguelike.
/// - WASD moves the hero on the XZ plane.
/// - The hero faces the mouse cursor.
/// - Left click fires the Primary Weapon, right click fires the Secondary Weapon.
///   Both trigger the same default Attack animation; the actual hit is whatever
///   the equipped Weapon spawns (sword slash, shield throw, ...).
///
/// Setup checklist on the Hero GameObject:
///   * Rigidbody  (Use Gravity = on, the script will freeze X/Z rotation)
///   * Collider   (e.g. CapsuleCollider sized to the hero)
///   * Tag        "Player" (optional, but enemies look for it)
///   * Layer      e.g. "Player"
///   * Weapon components (e.g. SwordWeapon, ShieldWeapon) on this object or a child.
///     Drag the ones you want into Primary Weapon and Secondary Weapon below.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Hero : MonoBehaviour
{
    [Header("Stats")]
    [Tooltip("Maximum hit points.")]
    public float maxHP = 100f;
    [Tooltip("Movement speed in units per second.")]
    public float moveSpeed = 5f;

    [Header("Weapons")]
    [Tooltip("Fired on left click. Drag a Weapon component (e.g. SwordWeapon) here.")]
    public Weapon primaryWeapon;
    [Tooltip("Fired on right click. Drag a Weapon component (e.g. ShieldWeapon) here.")]
    public Weapon secondaryWeapon;

    [Header("Aim")]
    [Tooltip("Y-height of the imaginary ground plane the mouse aim is projected onto. Match the hero's feet/ground height.")]
    public float aimPlaneY = 0f;

    [Header("Animation")]
    [Tooltip("Optional. If empty, the script grabs the first Animator found in children. The Animator Controller should expose: float 'Speed', trigger 'Attack', trigger 'Die'.")]
    public Animator animator;
    [Tooltip("Smoothing time for the Speed parameter so the walk anim eases in/out instead of popping.")]
    public float animSpeedDamping = 0.08f;

    [Header("Debug / Read-only")]
    [SerializeField] private float currentHP;

    // Hashed Animator parameter names (faster than string lookup every frame).
    private static readonly int kSpeed  = Animator.StringToHash("Speed");
    private static readonly int kAttack = Animator.StringToHash("Attack");
    private static readonly int kDie    = Animator.StringToHash("Die");

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public bool IsDead { get; private set; }

    /// <summary>
    /// Global reference to the active Hero. Other scripts (enemies, camera,
    /// HUD) can use Hero.Instance instead of FindObjectOfType. Set in Awake,
    /// cleared in OnDestroy.
    /// </summary>
    public static Hero Instance { get; private set; }

    // --- internals ---
    private Rigidbody rb;
    private Camera cam;
    private Vector3 moveInput;

    private void Awake()
    {
        Instance = this;

        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (animator == null) animator = GetComponentInChildren<Animator>();

        cam = Camera.main;
        currentHP = maxHP;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

        // --- Attack: dispatch to weapons every frame the button is held.
        // Each weapon's own cooldown rate-limits how often it actually fires. ---
        if (Input.GetMouseButton(0) && primaryWeapon != null)
        {
            if (primaryWeapon.TryFire(this) && animator != null) animator.SetTrigger(kAttack);
        }
        if (Input.GetMouseButton(1) && secondaryWeapon != null)
        {
            if (secondaryWeapon.TryFire(this) && animator != null) animator.SetTrigger(kAttack);
        }

        // Drive the walk/idle animation off how hard the player is pushing the stick/keys.
        if (animator != null)
        {
            float speed01 = moveInput.magnitude; // 0 when idle, 1 when running
            animator.SetFloat(kSpeed, speed01, animSpeedDamping, Time.deltaTime);
        }
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
        if (rb != null) rb.velocity = Vector3.zero;
        if (animator != null)
        {
            animator.SetFloat(kSpeed, 0f);
            animator.SetTrigger(kDie);
        }
        // TODO: show game-over UI, restart prompt, etc.
        Debug.Log("Hero died.");
    }

}
