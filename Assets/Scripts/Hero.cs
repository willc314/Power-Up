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

    /// <summary>
    /// Weapons can multiply the hero's effective move speed (e.g. the Bow slows
    /// the hero while charging). Reset to 1f after the slowing condition ends.
    /// </summary>
    [System.NonSerialized] public float speedMultiplier = 1f;

    [Header("Weapons")]
    [Tooltip("Fired on left click. Drag a Weapon component (e.g. SwordWeapon) here. Overwritten on spawn if 'Randomize Weapons On Spawn' is on.")]
    public Weapon primaryWeapon;
    [Tooltip("Fired on right click. Drag a Weapon component (e.g. ShieldWeapon) here. Overwritten on spawn if 'Randomize Weapons On Spawn' is on.")]
    public Weapon secondaryWeapon;
    [Tooltip("If true, on spawn the hero is given a single random weapon (primary only) drawn from the pool below — or, if that's empty, from every Weapon component on the hero. The secondary slot stays empty.")]
    public bool randomizeWeaponsOnSpawn = true;
    [Tooltip("Optional pool of weapons the random pick chooses from. Leave empty to use every Weapon component found on this hero (and its children).")]
    public System.Collections.Generic.List<Weapon> randomWeaponPool = new System.Collections.Generic.List<Weapon>();

    [Header("Dash")]
    [Tooltip("Key that triggers a dash. Default Space.")]
    public KeyCode dashKey = KeyCode.Space;
    [Tooltip("Speed during the dash, in units/sec.")]
    public float dashSpeed = 22f;
    [Tooltip("How long the dash lasts. Multiply by dashSpeed for total dash distance.")]
    public float dashDuration = 0.18f;
    [Tooltip("Cooldown between dashes, in seconds.")]
    public float dashCooldown = 1.0f;
    [Tooltip("If true, dashing cancels in-progress weapon state (bow charge, etc.). Recommended.")]
    public bool dashInterruptsWeapons = true;

    [Header("Dash Particles")]
    [Tooltip("Spawn dust particles flying behind the hero during the dash.")]
    public bool dashParticles = true;
    [Tooltip("How many dust particles per second during the dash. 0 = none.")]
    public float dashParticlesPerSecond = 80f;
    [Tooltip("Color of the dust particles. Light brown reads as dirt; grey as stone.")]
    public Color dashParticleColor = new Color(0.65f, 0.55f, 0.42f);
    [Tooltip("Initial speed of each dust particle.")]
    public float dashParticleSpeed = 4f;
    [Tooltip("Edge length of each particle.")]
    public float dashParticleSize = 0.12f;
    [Tooltip("Seconds before each particle self-destroys.")]
    public float dashParticleLifetime = 0.4f;
    [Tooltip("Cone spread angle in degrees for the dust spray.")]
    public float dashParticleSpread = 35f;
    [Tooltip("Height above ground where particles spawn (keeps them at the hero's feet).")]
    public float dashParticleHeight = 0.1f;

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
    private DamageFlash damageFlash;

    // Dash state
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;
    private float dashParticleAccumulator;
    public bool IsDashing => dashTimer > 0f;

    private void Awake()
    {
        Instance = this;

        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (animator == null) animator = GetComponentInChildren<Animator>();
        damageFlash = GetComponent<DamageFlash>();

        cam = Camera.main;
        currentHP = maxHP;

        if (randomizeWeaponsOnSpawn) PickRandomWeapons();
    }

    /// <summary>
    /// Replaces primaryWeapon with a random weapon drawn from randomWeaponPool
    /// (or, if that's empty, from every Weapon component attached to this hero
    /// or a child). The secondary slot is cleared so the hero spawns with a
    /// single weapon.
    /// </summary>
    private void PickRandomWeapons()
    {
        // Build the candidate pool. Inspector-provided list wins so designers
        // can blacklist weapons (e.g. exclude the bow if it isn't tuned yet).
        var pool = new System.Collections.Generic.List<Weapon>();
        if (randomWeaponPool != null && randomWeaponPool.Count > 0)
        {
            foreach (var w in randomWeaponPool) if (w != null) pool.Add(w);
        }
        else
        {
            pool.AddRange(GetComponentsInChildren<Weapon>(includeInactive: true));
        }

        if (pool.Count == 0)
        {
            Debug.LogWarning("[Hero] randomizeWeaponsOnSpawn is on but no Weapon components were found.");
            return;
        }

        primaryWeapon   = pool[Random.Range(0, pool.Count)];
        secondaryWeapon = null;
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

        // --- Dash ---
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
        if (Input.GetKeyDown(dashKey) && dashCooldownTimer <= 0f && !IsDashing) StartDash();

        // --- Attack: dispatch press/hold/release to the weapons. Auto-repeat
        // weapons (sword, dagger, crossbow, ...) only use OnFireHeld; the Bow
        // uses all three to implement charging.
        DispatchWeaponInput(0, primaryWeapon);
        DispatchWeaponInput(1, secondaryWeapon);

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

        if (IsDashing)
        {
            dashTimer -= Time.fixedDeltaTime;
            Vector3 step = dashDirection * dashSpeed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + step);
            EmitDashParticles();
        }
        else
        {
            Vector3 target = rb.position + moveInput * moveSpeed * speedMultiplier * Time.fixedDeltaTime;
            rb.MovePosition(target);
        }
    }

    private void EmitDashParticles()
    {
        if (!dashParticles || dashParticlesPerSecond <= 0f) return;
        dashParticleAccumulator += dashParticlesPerSecond * Time.fixedDeltaTime;
        Vector3 origin = transform.position + Vector3.up * dashParticleHeight;
        Vector3 backward = -dashDirection;
        while (dashParticleAccumulator >= 1f)
        {
            HitParticles.EmitBurst(origin, backward,
                count: 1,
                speed: dashParticleSpeed,
                lifetime: dashParticleLifetime,
                size: dashParticleSize,
                color: dashParticleColor,
                spreadAngle: dashParticleSpread,
                useGravity: true);
            dashParticleAccumulator -= 1f;
        }
    }

    private void StartDash()
    {
        // Direction: WASD if held, otherwise the hero's current facing.
        Vector3 dir = moveInput.sqrMagnitude > 0.01f ? moveInput : transform.forward;
        dir.y = 0f;
        dashDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        dashParticleAccumulator = 0f;

        if (dashInterruptsWeapons)
        {
            if (primaryWeapon != null)   primaryWeapon.OnInterrupted(this);
            if (secondaryWeapon != null) secondaryWeapon.OnInterrupted(this);
        }
    }

    private void DispatchWeaponInput(int button, Weapon w)
    {
        if (w == null) return;
        bool playAnim = false;
        if (Input.GetMouseButtonDown(button)) playAnim |= w.OnFireDown(this);
        if (Input.GetMouseButton(button))     playAnim |= w.OnFireHeld(this);
        if (Input.GetMouseButtonUp(button))   playAnim |= w.OnFireUp(this);
        if (playAnim && animator != null) animator.SetTrigger(kAttack);
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
        if (damageFlash != null) damageFlash.Flash();
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
