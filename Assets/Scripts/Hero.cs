using UnityEngine;
using UnityEngine.SceneManagement;
/// <summary>
/// Top-down hero controller for a Vampire Survivors-style roguelike.
/// - WASD moves the hero on the XZ plane.
/// - The hero faces the mouse cursor.
/// - Left click fires the Primary Weapon, right click fires the Secondary Weapon.
///
/// Weapon input:
///   * Primary Weapon uses mouse button 0
///   * Secondary Weapon uses mouse button 1
///   * Weapons receive press, hold, and release events so normal weapons and
///     charge weapons like Bow can both work.
///
/// Powerup flow:
///   * Hero touches PowerUp
///   * PowerUp calls ApplyPowerUp()
///   * PowerUpChoiceUI opens if it exists
///   * Upgrade choice upgrades matching weapon damage
///   * Replace choice swaps the active primary or secondary weapon
///   * If the replaced weapon was maxed, the new weapon becomes maxed too
///
/// Visual weapon swapping:
///   The actual attack changes by assigning Primary Weapon or Secondary Weapon.
///   Optional hand visual fields let you hide/show the sword, shield, bow, dagger,
///   etc. that are attached to the Hero model so the visible weapon matches
///   the active weapon.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Hero : MonoBehaviour
{
    public enum HeroStatBoostMode
    {
        Random,
        MaxHP,
        MoveSpeed,
        HealthRegen,
        DamageBoost,
        CritRate,
        CritDamage
    }

    [Header("Debug")]
    [Tooltip("Debug toggle: when true, the hero takes no damage from any source (TakeDamage and SetCurrentHP both early-return). Toggle live in the Inspector during play to test boss patterns without dying.")]
    public bool debugInvincible = false;

    [Header("Stats")]
    [Tooltip("Maximum hit points.")]
    public float maxHP = 100f;

    [Tooltip("Movement speed in units per second.")]
    public float moveSpeed = 5f;

    /// <summary>
    /// Weapons can multiply the hero's effective move speed.
    /// Bow and DeathBeam use this to slow or stop the hero while charging/firing.
    /// </summary>
    [System.NonSerialized] public float speedMultiplier = 1f;

    [Header("Powerup Stat Boosts")]
    [Tooltip("Which Hero stat gets boosted when a weapon is already maxed.")]
    public HeroStatBoostMode maxedWeaponBoostMode = HeroStatBoostMode.Random;
    [Tooltip("How much max HP increases from a stat boost.")]
    public float maxHPBoostAmount = 10f;
    [Tooltip("How much movement speed increases from a stat boost.")]
    public float moveSpeedBoostAmount = 0.25f;
    [Tooltip("Optional movement speed cap so speed boosts do not get ridiculous.")]
    public float maxMoveSpeed = 10f;
    [Tooltip("Fraction the dash cooldown shrinks per Move Speed boost. 0.02 = -2% per pickup, with diminishing returns since each step is a percentage of the current cooldown.")]
    [Range(0f, 1f)] public float dashCooldownReductionPercent = 0.02f;
    [Tooltip("Floor for dashCooldown when applying the Move Speed boost so the dash never becomes truly free.")]
    public float minDashCooldown = 0.5f;
    [Tooltip("Current health regen, in HP per second. Boosts add to this.")]
    public float healthRegenPerSecond = 0f;
    [Tooltip("How much HP/sec regen increases per boost.")]
    public float healthRegenBoostAmount = 0.5f;
    [Tooltip("Cap on healthRegenPerSecond.")]
    public float maxHealthRegenPerSecond = 10f;

    [Header("I am Tank! General Buff (level-up unlock)")]
    [Tooltip("Set to true by IAmTankUpgrade.Apply when the player picks the buff. Unlocks the MMB ability and switches HP/regen scaling rules.")]
    public bool iAmTankActive = false;
    [Tooltip("Per-MaxHP-boost exponential multiplier replacement when iAmTankActive. 0.05 = MaxHP scales ×1.05 per pickup instead of the default flat +maxHPBoostAmount. Compounds across pickups.")]
    public float iAmTankHpExponentialPerBoost = 0.05f;
    [Tooltip("Multiplier applied to BOTH the passive regen tick AND each HealthRegen boost while iAmTankActive. 2 = regen and regen-boost gains are doubled.")]
    public float iAmTankRegenMultiplier = 2f;
    [Tooltip("Outgoing damage multiplier active for the duration of an I-am-Tank shield. Stacks multiplicatively on top of damageMultiplier and crit. Multiple stacked shields don't increase this — it's a flat 'shield up' buff.")]
    public float iAmTankDamageMultiplier = 1.30f;
    [Tooltip("Cooldown (seconds) between shield ability casts.")]
    public float iAmTankAbilityCooldown = 30f;
    [Tooltip("Shield duration (seconds). Absorbs ONE incoming hit of any size, then breaks. Recasting within the last few seconds before expiry refreshes duration to full.")]
    public float iAmTankShieldDuration = 35f;
    [Tooltip("Heal applied as a fraction of MaxHP each time the ability is cast.")]
    [Range(0f, 1f)] public float iAmTankHealFraction = 0.20f;
    [Tooltip("Optional shield visual (Hovl Magic Shield Blue). Spawned as a child of the hero while the shield is up; destroyed on break / expiry.")]
    public GameObject tankShieldPrefab;
    [Tooltip("Uniform scale applied to the spawned shield visual. Tweak to fit the hero's body size.")]
    public float tankShieldVisualScale = 1.2f;
    [Tooltip("Vertical offset for the shield visual relative to hero pivot.")]
    public float tankShieldVisualYOffset = 0.9f;
    [Tooltip("Seconds to simulate the shield prefab forward into its lifecycle before freezing it. Tune until the freeze lands on a fully-formed shield silhouette — too low captures the intro animation, too high captures the outro / fade-out. Most shield packs settle into their 'active' pose ~1-2s in.")]
    public float tankShieldFreezeTime = 1.5f;

    [Header("Meteor General Augment (level-up unlock)")]
    [Tooltip("Set to true by MeteorUpgrade.Apply when the player picks the augment. While true, every CRIT attack from any weapon has meteorChanceOnCrit chance to call down a meteor on the first enemy that fire-event hits.")]
    public bool meteorEnabled = false;
    [Tooltip("Chance (0..1) per crit attack to arm a meteor on the resulting projectile/swing. Default 0.5 = 50%.")]
    [Range(0f, 1f)] public float meteorChanceOnCrit = 0.5f;
    [Tooltip("Damage multiplier applied to the attack's per-hit damage to compute the meteor's AOE damage. Default 4 = quadrupled damage.")]
    public float meteorDamageMultiplier = 4f;
    [Tooltip("Meteor visual prefab spawned at the impact point. Drop in vfx_MeteorRain_01 (or another) from GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs, or ppfxRay from ParticleProFX.")]
    public GameObject meteorVfxPrefab;
    [Tooltip("Local Euler rotation applied to the spawned meteor VFX so its trail points in the right direction. Most ray/beam prefabs (e.g. ppfxRay) point along their local +Z; setting this to (90,0,0) re-aims that axis along world -Y, i.e. straight down. Drop-in self-contained meteor prefabs (vfx_MeteorRain_01) usually want (0,0,0). Tweak in 90° increments — (-90,0,0), (0,90,0), etc — until the trail visually falls from sky to ground.")]
    public Vector3 meteorVfxRotationOffset = new Vector3(90f, 0f, 0f);
    [Tooltip("Delay (seconds) between the meteor spawn (which immediately starts the prefab's visual fall) and the AOE damage application. Tune this to match the prefab's natural impact moment so the damage lands when the visual lands.")]
    public float meteorFallDuration = 0.7f;
    [Tooltip("AOE damage radius around the meteor's impact point. Every enemy whose collider overlaps this sphere takes the full meteor damage once.")]
    public float meteorAOERadius = 3f;

    [Header("Damage Modifiers")]
    [Tooltip("Multiplier applied to ALL weapon damage at attack time. 1 = no change.")]
    public float damageMultiplier = 1f;
    [Tooltip("How much damageMultiplier grows per Damage Boost. 0.15 = +15% per pickup.")]
    public float damageBoostAmount = 0.15f;
    [Tooltip("Cap on damageMultiplier.")]
    public float maxDamageMultiplier = 5f;

    [Tooltip("Probability (0..1) that an attack rolls a critical hit, multiplying damage by critDamage.")]
    [Range(0f, 1f)] public float critRate = 0f;
    [Tooltip("How much critRate grows per Crit Rate boost. 0.05 = +5% per pickup.")]
    public float critRateBoostAmount = 0.05f;
    [Tooltip("Cap on critRate.")]
    [Range(0f, 1f)] public float maxCritRate = 0.6f;

    [Tooltip("Damage multiplier applied on a critical hit. 2 = double damage on crits.")]
    public float critDamage = 2f;
    [Tooltip("How much critDamage grows per Crit Damage boost.")]
    public float critDamageBoostAmount = 0.25f;
    [Tooltip("Cap on critDamage.")]
    public float maxCritDamage = 6f;

    [Header("Active Weapons")]
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
    [Tooltip("If true, the hero takes no damage during a dash (plus the extension below).")]
    public bool dashInvulnerability = true;
    [Tooltip("Extra seconds of i-frames added after the dash ends, so a collision landing on the last frame of the dash doesn't slip through.")]
    public float dashInvulnerabilityExtension = 0.05f;
    [Tooltip("If true, the hero passes through enemies during dash (no physical collision, so the player can't get shoved or stopped).")]
    public bool dashPhasesThroughEnemies = true;
    [Tooltip("Layer name enemies live on. Used to selectively ignore collision with the hero during dash. Leave 'Enemy' unless your project uses a different name.")]
    public string enemyLayerName = "Enemy";
    [Tooltip("Distance to keep from the arena edge during movement. Prevents tunneling through the boundary wall when the dash or move speed would otherwise carry the hero past it in one physics tick.")]
    public float dashArenaEdgeMargin = 0.5f;

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

    [Header("Weapon Components On Hero")]
    [Tooltip("SwordWeapon component attached to this Hero.")]
    public SwordWeapon swordWeapon;

    [Tooltip("ShieldWeapon component attached to this Hero.")]
    public ShieldWeapon shieldWeapon;

    [Tooltip("BowWeapon component attached to this Hero.")]
    public BowWeapon bowWeapon;

    [Tooltip("DaggerWeapon component attached to this Hero.")]
    public DaggerWeapon daggerWeapon;

    [Tooltip("CrossbowWeapon component attached to this Hero.")]
    public CrossbowWeapon crossbowWeapon;

    [Tooltip("GrenadeWeapon component attached to this Hero.")]
    public GrenadeWeapon grenadeWeapon;

    [Header("Hand Visuals")]
    [Tooltip("Optional sword model already attached to the Hero rig/hand.")]
    public GameObject swordHandVisual;

    [Tooltip("Optional shield model already attached to the Hero rig/hand.")]
    public GameObject shieldHandVisual;

    [Tooltip("Optional bow model already attached to the Hero rig/hand.")]
    public GameObject bowHandVisual;

    [Tooltip("Optional dagger model already attached to the Hero rig/hand.")]
    public GameObject daggerHandVisual;

    [Tooltip("Optional crossbow model already attached to the Hero rig/hand.")]
    public GameObject crossbowHandVisual;

    [Tooltip("Optional grenade model already attached to the Hero rig/hand.")]
    public GameObject grenadeHandVisual;

    [Header("Aim")]
    [Tooltip("Y-height of the imaginary ground plane the mouse aim is projected onto.")]
    public float aimPlaneY = 0f;

    [Header("End-of-Run Scenes")]
    [Tooltip("Scene name loaded when the hero dies normally (game over).")]
    public string gameOverSceneName = "EndScreen";
    [Tooltip("Scene name loaded when the hero kills the SlimeGod final boss. Defaults to 'VictoryScreen' — make sure such a scene exists and is in Build Settings.")]
    public string victorySceneName = "VictoryScreen";
    [Tooltip("Seconds of pause AFTER the boss dies before the end sequence (death-cam zoom + scene load) starts. Lets the kill read on screen.")]
    public float victoryEndSequenceDelay = 3f;
    [Tooltip("Seconds for the end-sequence camera zoom. The scene load happens at the end of this window.")]
    public float endSequenceCameraTime = 4f;

    [Header("Animation")]
    [Tooltip("Optional. If empty, the script grabs the first Animator found in children.")]
    public Animator animator;

    [Tooltip("Smoothing time for the Speed parameter.")]
    public float animSpeedDamping = 0.08f;

    [Header("Debug / Read-only")]
    [SerializeField] private float currentHP;

    private static readonly int kSpeed = Animator.StringToHash("Speed");
    private static readonly int kAttack = Animator.StringToHash("Attack");
    private static readonly int kDie = Animator.StringToHash("Die");

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public bool IsDead { get; private set; }

    public static Hero Instance { get; private set; }

    // ---- Snapshot of starting stats ----
    // Captured in Awake so the StatsMenu can show "original → current" for
    // every value the powerup boosts modify. These are read-only after Awake.
    public float OriginalMaxHP                { get; private set; }
    public float OriginalMoveSpeed            { get; private set; }
    public float OriginalHealthRegen          { get; private set; }
    public float OriginalDamageMultiplier     { get; private set; }
    public float OriginalCritRate             { get; private set; }
    public float OriginalCritDamage           { get; private set; }
    public float OriginalDashCooldown         { get; private set; }
    public float OriginalDashIFrameMultiplier { get; private set; }
    public float DashIFrameMultiplier => dashIFrameMultiplier;

    /// <summary>Fires once when the hero dies. Subscribers (e.g. EnemyAnimator) can react globally.</summary>
    public static event System.Action OnHeroDied;

    private Rigidbody rb;
    private Camera cam;
    private Vector3 moveInput;
    private DamageFlash damageFlash;

    /// <summary>
    /// Read-only access to the hero's WASD input vector (XZ-only). Useful
    /// for systems that need to know where the player is TRYING to move
    /// rather than where they're facing — e.g. the SlimeGod's punish volley
    /// aims its spawn arc along this direction so it isn't fooled by the
    /// cursor-driven facing direction.
    /// </summary>
    public Vector3 MoveInput => moveInput;

    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;
    private float dashParticleAccumulator;
    private float invulnerabilityTimer;
    /// <summary>
    /// Multiplier on the dash i-frame window. Shrinks per MoveSpeed boost in
    /// lockstep with the dashCooldown reduction so faster dashing also means
    /// shorter invulnerability per dash.
    /// </summary>
    private float dashIFrameMultiplier = 1f;
    private int   cachedEnemyLayer = -1;
    private bool  enemyCollisionIgnored;
    private ArenaGenerator cachedArena;
    public bool IsDashing => dashTimer > 0f;

    /// <summary>Seconds left before the next dash is allowed. 0 = ready.</summary>
    public float DashCooldownRemaining => Mathf.Max(0f, dashCooldownTimer);

    /// <summary>
    /// True while the hero is invulnerable to damage. Currently set by the
    /// dash (covers the dash duration plus dashInvulnerabilityExtension).
    /// Other code paths (Hero.TakeDamage) early-out when this is true.
    /// </summary>
    public bool IsInvulnerable => dashInvulnerability && invulnerabilityTimer > 0f;

    // ---- I am Tank! runtime state ----
    private float tankCooldownTimer;          // seconds remaining before MMB can re-cast (0 = ready)
    private float tankShieldTimer;            // seconds remaining on active shield (0 = no shield)
    private GameObject tankShieldInstance;    // spawned shield visual (parented to hero)

    // ---- Zenith curse retroactive refunds ----
    // While the Zenith curse is active, hero stat boosts apply at half
    // efficiency and the missing half is banked here per-stat. When
    // SwordWeapon.ApplyZenith fires (curse ends), RefundZenithCurseGains()
    // applies all banked deltas so the player retroactively gets the
    // full-strength versions of every powerup taken during the curse.
    private float pendingHPRefund;
    private float pendingRegenRefund;
    private float pendingDamageMultRefund;
    private float pendingCritRateRefund;
    private float pendingCritDamageRefund;
    private float pendingMoveSpeedRefund;
    private float pendingDashCooldownRefund;  // POSITIVE = subtract more from dashCooldown
    /// <summary>
    /// True while the Zenith curse is active: the player has chosen the
    /// upgrade but the four stat thresholds + boss-defeat gate haven't
    /// been satisfied yet. Drives the 1/4 outgoing damage, 2× incoming
    /// damage, 50% powerup efficiency, and weapon-switch lockout.
    /// </summary>
    public bool IsZenithCursed
    {
        get
        {
            SwordWeapon sw = GetWeaponComponentForType(eWeaponType.sword) as SwordWeapon;
            return sw != null && sw.zenithUnlocked && !sw.zenithApplied;
        }
    }

    /// <summary>True while the I-am-Tank shield is up. While true, ComputeAttackDamage applies iAmTankDamageMultiplier and TakeDamage absorbs the next hit.</summary>
    public bool IsTankShieldActive => tankShieldTimer > 0f;
    /// <summary>0..1 progress for HUD ring; 1 = ready.</summary>
    public float TankAbilityCooldownProgress => iAmTankAbilityCooldown <= 0f ? 1f : Mathf.Clamp01(1f - tankCooldownTimer / iAmTankAbilityCooldown);
    /// <summary>Seconds remaining on the active shield, 0 if none.</summary>
    public float TankShieldTimeRemaining => Mathf.Max(0f, tankShieldTimer);

    private void Awake()
    {
        Instance = this;

        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // Cache the enemy layer index up front so dash phase-through can
        // toggle the Physics matrix in O(1).
        cachedEnemyLayer = LayerMask.NameToLayer(enemyLayerName);
        if (cachedEnemyLayer < 0 && dashPhasesThroughEnemies)
            Debug.LogWarning("[Hero] dashPhasesThroughEnemies is on but layer '" + enemyLayerName + "' doesn't exist; phase-through will be a no-op.");

        // Cache the arena so the dash can clamp inside its bounds rather
        // than tunneling through the wall colliders at high speed.
        cachedArena = FindObjectOfType<ArenaGenerator>();

        CacheWeaponComponents();
        ConfigureWeaponTypes();

        damageFlash = GetComponent<DamageFlash>();

        cam = Camera.main;
        currentHP = maxHP;

        // Snapshot starting stats so the StatsMenu can show original → current.
        OriginalMaxHP                = maxHP;
        OriginalMoveSpeed            = moveSpeed;
        OriginalHealthRegen          = healthRegenPerSecond;
        OriginalDamageMultiplier     = damageMultiplier;
        OriginalCritRate             = critRate;
        OriginalCritDamage           = critDamage;
        OriginalDashCooldown         = dashCooldown;
        OriginalDashIFrameMultiplier = dashIFrameMultiplier;

        if (randomizeWeaponsOnSpawn) PickRandomWeapons();
        else RefreshWeaponVisuals();
    }

    private void PickRandomWeapons()
    {
        var pool = new System.Collections.Generic.List<Weapon>();

        if (randomWeaponPool != null && randomWeaponPool.Count > 0)
        {
            foreach (var w in randomWeaponPool)
            {
                if (w != null)
                    pool.Add(w);
            }
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

        primaryWeapon = pool[Random.Range(0, pool.Count)];
        secondaryWeapon = null;

        RefreshWeaponVisuals();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        // Defensive: never leave the Physics matrix toggled if we get destroyed
        // mid-dash (scene transitions, restarts, etc.).
        SetEnemyCollisionIgnored(false);
    }

    private void OnDisable()
    {
        SetEnemyCollisionIgnored(false);
    }

    private void Update()
    {
        if (IsDead)
            return;

        if (IsFrozenForVictory)
            return;

        if (Time.timeScale == 0f)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        moveInput = new Vector3(h, 0f, v).normalized;

        FaceMouse();

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;
        if (invulnerabilityTimer > 0f)
            invulnerabilityTimer -= Time.deltaTime;

        if (Input.GetKeyDown(dashKey) && dashCooldownTimer <= 0f && !IsDashing)
            StartDash();

        DispatchWeaponInput(0, primaryWeapon);
        DispatchWeaponInput(1, secondaryWeapon);

        // Passive health regen from hero stat boosts. While I-am-Tank is
        // active the regen rate is doubled (per buff spec).
        if (healthRegenPerSecond > 0f && currentHP < maxHP)
        {
            float rate = healthRegenPerSecond;
            if (iAmTankActive) rate *= Mathf.Max(0f, iAmTankRegenMultiplier);
            currentHP = Mathf.Min(maxHP, currentHP + rate * Time.deltaTime);
        }

        // ---- I am Tank! ability tick ----
        if (iAmTankActive)
        {
            if (tankCooldownTimer > 0f) tankCooldownTimer -= Time.deltaTime;
            if (tankShieldTimer  > 0f)
            {
                tankShieldTimer -= Time.deltaTime;
                if (tankShieldTimer <= 0f) BreakTankShield();
            }
            // MMB to cast/refresh. Old Input Manager: button index 2.
            if (Input.GetMouseButtonDown(2) && tankCooldownTimer <= 0f && !IsDead)
                CastTankAbility();
        }

        if (animator != null)
        {
            float speed01 = moveInput.magnitude;
            animator.SetFloat(kSpeed, speed01, animSpeedDamping, Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (IsDead || IsFrozenForVictory || Time.timeScale == 0f)
            return;

        if (IsDashing)
        {
            dashTimer -= Time.fixedDeltaTime;
            Vector3 step = dashDirection * dashSpeed * Time.fixedDeltaTime;
            Vector3 target = ClampToArena(rb.position + step);
            rb.MovePosition(target);
            EmitDashParticles();
            if (dashTimer <= 0f)
            {
                // Dash just ended this tick — re-enable physics collision
                // with enemies. (i-frames keep ticking down on their own
                // timer for the small grace period.)
                SetEnemyCollisionIgnored(false);
            }
        }
        else
        {
            // Zero residual velocity each tick so charger-style enemies (the
            // mushroom dash) don't keep shoving the hero across the floor
            // after the impact. Without this, the rigidbody picks up momentum
            // from the collision and slides for several frames before drag
            // brings it to rest.
            rb.velocity = Vector3.zero;

            // Clamp normal movement too — boosted moveSpeed can otherwise
            // push past the wall colliders in a single tick the same way the
            // dash can.
            Vector3 target = ClampToArena(rb.position + moveInput * moveSpeed * speedMultiplier * Time.fixedDeltaTime);
            rb.MovePosition(target);
        }
    }

    /// <summary>
    /// Clamp <paramref name="target"/> to the arena bounds (with edge margin)
    /// so high-speed movement can't tunnel through the boundary walls in one
    /// physics tick. Looks up the arena lazily if it wasn't cached yet.
    /// </summary>
    private Vector3 ClampToArena(Vector3 target)
    {
        if (cachedArena == null) cachedArena = FindObjectOfType<ArenaGenerator>();
        if (cachedArena == null) return target;

        float half = cachedArena.arenaSize * 0.5f - dashArenaEdgeMargin;
        target.x = Mathf.Clamp(target.x, -half, half);
        target.z = Mathf.Clamp(target.z, -half, half);
        return target;
    }

    /// <summary>
    /// Toggle physics collision between the hero's layer and the enemy
    /// layer. Used by the dash to phase through enemies. Idempotent — safe
    /// to call repeatedly with the same value.
    /// </summary>
    private void SetEnemyCollisionIgnored(bool ignore)
    {
        if (!dashPhasesThroughEnemies) return;
        if (cachedEnemyLayer < 0) return;
        if (enemyCollisionIgnored == ignore) return;

        Physics.IgnoreLayerCollision(gameObject.layer, cachedEnemyLayer, ignore);
        enemyCollisionIgnored = ignore;
    }

    private void EmitDashParticles()
    {
        if (!dashParticles || dashParticlesPerSecond <= 0f)
            return;

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
        Vector3 dir = moveInput.sqrMagnitude > 0.01f ? moveInput : transform.forward;
        dir.y = 0f;

        dashDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        dashParticleAccumulator = 0f;
        // Cover the whole dash plus the small grace period afterward, then
        // scale by the cumulative MoveSpeed-boost shrinkage so faster dashing
        // = shorter invulnerability per dash.
        float baseWindow = dashDuration + Mathf.Max(0f, dashInvulnerabilityExtension);
        invulnerabilityTimer = baseWindow * dashIFrameMultiplier;

        // Phase through enemies for the duration of the dash. This is restored
        // when dashTimer hits zero in FixedUpdate (and defensively in
        // OnDisable/OnDestroy in case anything cuts the dash short).
        SetEnemyCollisionIgnored(true);

        if (dashInterruptsWeapons)
        {
            if (primaryWeapon != null)
                primaryWeapon.OnInterrupted(this);

            if (secondaryWeapon != null)
                secondaryWeapon.OnInterrupted(this);
        }
    }

    private void CacheWeaponComponents()
    {
        if (swordWeapon == null)
            swordWeapon = GetComponent<SwordWeapon>();

        if (shieldWeapon == null)
            shieldWeapon = GetComponent<ShieldWeapon>();

        if (bowWeapon == null)
            bowWeapon = GetComponent<BowWeapon>();

        if (daggerWeapon == null)
            daggerWeapon = GetComponent<DaggerWeapon>();

        if (crossbowWeapon == null)
            crossbowWeapon = GetComponent<CrossbowWeapon>();

        if (grenadeWeapon == null)
            grenadeWeapon = GetComponent<GrenadeWeapon>();
    }

    private void ConfigureWeaponTypes()
    {
        if (swordWeapon != null)
        {
            swordWeapon.weaponName = string.IsNullOrEmpty(swordWeapon.weaponName) || swordWeapon.weaponName == "Weapon" ? "Sword" : swordWeapon.weaponName;
            swordWeapon.weaponType = eWeaponType.sword;
        }

        if (shieldWeapon != null)
        {
            shieldWeapon.weaponName = string.IsNullOrEmpty(shieldWeapon.weaponName) || shieldWeapon.weaponName == "Weapon" ? "Shield" : shieldWeapon.weaponName;
            shieldWeapon.weaponType = eWeaponType.shield;
        }

        if (bowWeapon != null)
        {
            bowWeapon.weaponName = string.IsNullOrEmpty(bowWeapon.weaponName) || bowWeapon.weaponName == "Weapon" ? "Bow" : bowWeapon.weaponName;
            bowWeapon.weaponType = eWeaponType.bow;
        }

        if (daggerWeapon != null)
        {
            daggerWeapon.weaponName = string.IsNullOrEmpty(daggerWeapon.weaponName) || daggerWeapon.weaponName == "Weapon" ? "Dagger" : daggerWeapon.weaponName;
            daggerWeapon.weaponType = eWeaponType.dagger;
        }

        if (crossbowWeapon != null)
        {
            crossbowWeapon.weaponName = string.IsNullOrEmpty(crossbowWeapon.weaponName) || crossbowWeapon.weaponName == "Weapon" ? "Crossbow" : crossbowWeapon.weaponName;
            crossbowWeapon.weaponType = eWeaponType.crossbow;
        }

        if (grenadeWeapon != null)
        {
            grenadeWeapon.weaponName = string.IsNullOrEmpty(grenadeWeapon.weaponName) || grenadeWeapon.weaponName == "Weapon" ? "Grenade" : grenadeWeapon.weaponName;
            grenadeWeapon.weaponType = eWeaponType.grenade;
        }
    }

    private void DispatchWeaponInput(int mouseButton, Weapon weapon)
    {
        if (weapon == null)
            return;

        // Sword dual-wield: when the SAME SwordWeapon component occupies
        // BOTH primary and secondary slots (post-Zenith-unlock), the
        // secondary mouse button (RMB) needs its own cooldown so the
        // player can spam-attack out of sync with LMB. Route the secondary
        // slot through TryFireSecondary, which uses the sword's
        // secondaryCooldownTimer instead of the shared cooldownTimer that
        // the primary slot drives. The check `weapon == primaryWeapon`
        // is enough to detect the dual-wield case — if both slots
        // reference the same component, dual-wielding is active.
        if (mouseButton == 1 && weapon == primaryWeapon && weapon is SwordWeapon dualSword)
        {
            // Sword fires on hold (auto-repeat). Skip the down/up paths since
            // the regular sword has no charging behavior.
            if (Input.GetMouseButton(1))
            {
                if (dualSword.TryFireSecondary(this) && animator != null)
                    animator.SetTrigger(kAttack);
            }
            return;
        }

        bool playedAttack = false;

        if (Input.GetMouseButtonDown(mouseButton))
            playedAttack |= weapon.OnFireDown(this);

        if (Input.GetMouseButton(mouseButton))
            playedAttack |= weapon.OnFireHeld(this);

        if (Input.GetMouseButtonUp(mouseButton))
            playedAttack |= weapon.OnFireUp(this);

        if (playedAttack && animator != null)
            animator.SetTrigger(kAttack);
    }

    private void FaceMouse()
    {
        if (cam == null)
        {
            cam = Camera.main;

            if (cam == null)
                return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, aimPlaneY, 0f));

        if (ground.Raycast(ray, out float dist))
        {
            Vector3 point = ray.GetPoint(dist);
            Vector3 dir = point - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    /// <summary>
    /// Project the mouse cursor onto the hero's aim-plane and return the
    /// world-space hit point. Returns false if the camera or the raycast
    /// missed (cursor is off-plane / behind the camera). Used by Sword
    /// Zenith to make the swing's elliptical major axis match the cursor's
    /// actual distance instead of a fixed orbit radius.
    /// </summary>
    public bool TryGetCursorWorldPosition(out Vector3 cursorWorld)
    {
        cursorWorld = Vector3.zero;
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, aimPlaneY, 0f));
        if (!ground.Raycast(ray, out float dist)) return false;
        cursorWorld = ray.GetPoint(dist);
        return true;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead)
            return;

        // Debug invincibility (Inspector toggle).
        if (debugInvincible)
            return;

        // Frozen during victory pause / win menu — don't take damage either.
        if (IsFrozenForVictory)
            return;

        // Dash i-frames: ignore damage while the invulnerability window is active.
        if (IsInvulnerable)
            return;

        // I am Tank! shield: absorb ONE hit of any size and pop. Per spec
        // the shield drops the entire incoming amount (not partial), which
        // also ends the +30% damage buff via IsTankShieldActive flipping
        // false. Triggers the same body/vignette flash as a real hit so
        // the player gets visual confirmation the shield broke.
        if (IsTankShieldActive)
        {
            BreakTankShield();
            if (damageFlash != null) damageFlash.Flash();
            if (HealthVignette.Instance != null) HealthVignette.Instance.Flash();
            return;
        }

        // Charging-bow damage reduction. Both slots are checked because the
        // player can hold the bow on either LMB or RMB; whichever bow is
        // currently charging wins (the higher reduction if both somehow are).
        float chargeReduction = 0f;
        if (primaryWeapon is BowWeapon bowA && bowA.IsCharging)
            chargeReduction = bowA.chargingDamageReduction;
        if (secondaryWeapon is BowWeapon bowB && bowB.IsCharging)
            chargeReduction = Mathf.Max(chargeReduction, bowB.chargingDamageReduction);
        if (chargeReduction > 0f)
            amount *= 1f - Mathf.Clamp01(chargeReduction);

        // Zenith curse: incoming damage doubled until the player completes
        // the trial. Applied AFTER bow charge reduction so the reduction
        // still meaningfully softens the doubled hit.
        if (IsZenithCursed)
            amount *= 2f;

        currentHP = Mathf.Max(0f, currentHP - amount);

        if (damageFlash != null)
            damageFlash.Flash();

        // Brief red vignette flash so the player gets a clear screen-edge
        // signal that they were hit, even when the body flash is hidden by
        // particles / weapons / camera angle.
        if (HealthVignette.Instance != null)
            HealthVignette.Instance.Flash();

        if (currentHP <= 0f)
            Die();
    }

    /// <summary>
    /// Force the hero down to a specific HP value, bypassing dash i-frames.
    /// Only ever LOWERS HP — calling with a target above current HP is a
    /// no-op so this can't accidentally heal. Used by the SlimeGod's spawn
    /// attack which is unavoidable by design — it slams the player down to
    /// 1 HP regardless of dash state. Dies if hp ≤ 0.
    /// </summary>
    public void SetCurrentHP(float hp)
    {
        if (IsDead) return;
        if (debugInvincible) return; // debug toggle bypasses the unavoidable spawn slam too
        float clamped = Mathf.Clamp(hp, 0f, maxHP);
        if (clamped >= currentHP) return; // never heal via this entry point
        currentHP = clamped;
        if (damageFlash != null) damageFlash.Flash();
        if (HealthVignette.Instance != null) HealthVignette.Instance.Flash();
        if (currentHP <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead)
            return;

        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }

    /// <summary>
    /// Cast the I-am-Tank shield ability. Heals 20% MaxHP, spawns/refreshes
    /// the shield visual, sets shield duration. Uses cooldown gates in the
    /// Update tick — this method assumes the cooldown check has already
    /// been done by the caller (Hero.Update on MMB press).
    ///
    /// Refresh semantics: pressing again while a shield is up overrides its
    /// remaining time with the full duration. This naturally supports the
    /// player using the ability in the last seconds of the shield to keep
    /// the +30% damage buff continuous (no gap means no damage-multiplier reset).
    /// </summary>
    private void CastTankAbility()
    {
        if (!iAmTankActive || IsDead) return;
        // Heal — 20% of MaxHP, capped at MaxHP.
        Heal(maxHP * Mathf.Clamp01(iAmTankHealFraction));
        // Reset cooldown.
        tankCooldownTimer = Mathf.Max(0f, iAmTankAbilityCooldown);
        // Refresh-or-start shield duration. This single line covers both
        // "first cast" (timer was 0) and "recast within last 5s" (timer was
        // small) — full duration always wins.
        tankShieldTimer = Mathf.Max(0.01f, iAmTankShieldDuration);
        // Spawn the visual if not already spawned. Recasts keep the same
        // instance (no flicker between despawn / respawn).
        if (tankShieldInstance == null && tankShieldPrefab != null)
        {
            Vector3 pos = transform.position + Vector3.up * tankShieldVisualYOffset;
            tankShieldInstance = Instantiate(tankShieldPrefab, pos, transform.rotation, transform);
            tankShieldInstance.transform.localPosition = Vector3.up * tankShieldVisualYOffset;
            tankShieldInstance.transform.localRotation = Quaternion.identity;
            tankShieldInstance.transform.localScale = Vector3.one * Mathf.Max(0.0001f, tankShieldVisualScale);
            // Tame the prefab: kill physics interference (so the shield
            // can't shove enemies / hero) AND simulate the prefab forward
            // to its "fully formed shield" pose, then freeze it. Looping
            // didn't work for shield packs whose ParticleSystem itself has
            // built-in cycle behavior (timed bursts, sub-emitters keyed
            // off cycle end, particle lifetimes producing visible waves)
            // — Simulate + Pause halts the entire system mid-animation
            // so what the player sees is a static silhouette of the shield
            // instead of a recurring intro→outro sequence. Tune
            // tankShieldFreezeTime in the inspector to dial in the frame.
            VfxHelpers.DisablePhysicsInterference(tankShieldInstance);
            VfxHelpers.FreezeVfxAtTime(tankShieldInstance, tankShieldFreezeTime);
        }
    }

    /// <summary>
    /// End the shield: clear timer, destroy visual, drop the +30% damage
    /// buff (it's tied to IsTankShieldActive). Called when the shield
    /// absorbs a hit OR when its duration expires.
    /// </summary>
    private void BreakTankShield()
    {
        tankShieldTimer = 0f;
        if (tankShieldInstance != null)
        {
            Destroy(tankShieldInstance);
            tankShieldInstance = null;
        }
    }

    public void ApplyPowerUp(eWeaponType type)
    {
        if (IsDead)
            return;

        // Cancel any in-progress weapon state before the panel pauses time.
        // Otherwise the bow/crossbow visual the player was holding stays in
        // the world while paused — and the OnFireUp event never fires after
        // unpause if the player released during the pause.
        if (primaryWeapon != null)   primaryWeapon.OnInterrupted(this);
        if (secondaryWeapon != null) secondaryWeapon.OnInterrupted(this);
        speedMultiplier = 1f;

        if (PowerUpChoiceUI.Instance != null)
        {
            PowerUpChoiceUI.Instance.Show(this, type);
            return;
        }

        Weapon matchingWeapon = GetWeaponOfType(type);

        if (matchingWeapon != null && !matchingWeapon.IsDamageMaxed)
            UpgradeWeaponDamage(matchingWeapon);
        else
            ApplyHeroStatBoost();
    }

    public Weapon GetWeaponOfType(eWeaponType type)
    {
        if (type == eWeaponType.none)
            return null;

        if (primaryWeapon != null && primaryWeapon.weaponType == type)
            return primaryWeapon;

        if (secondaryWeapon != null && secondaryWeapon.weaponType == type)
            return secondaryWeapon;

        return null;
    }

    public void UpgradeWeaponDamage(Weapon weapon)
    {
        if (weapon == null)
            return;

        if (weapon.TryUpgradeDamage())
            Debug.Log("Upgraded " + weapon.weaponName + ".");
        else
            ApplyHeroStatBoost();
    }

    public void BeginReplaceWeaponChoice(eWeaponType newType)
    {
        bool replacePrimary = ShouldReplacePrimaryByDefault(newType);
        ReplaceWeaponSlot(replacePrimary, newType);
    }

    public void ReplaceWeaponSlot(bool replacePrimary, eWeaponType newType)
    {
        Weapon oldWeapon = replacePrimary ? primaryWeapon : secondaryWeapon;
        Weapon newWeapon = GetWeaponComponentForType(newType);

        if (newWeapon == null)
        {
            Debug.LogWarning("Hero: no weapon component exists for " + newType + ". Taking stat boost instead.");
            ApplyHeroStatBoost();
            return;
        }

        // Zenith curse: lockout to swords only. The player can still equip
        // a SECOND sword (that's what completes dual-wield), but anything
        // else is rejected so the player has to commit to the sword trial.
        if (IsZenithCursed && newType != eWeaponType.sword)
        {
            Debug.Log("[Zenith Curse] Cannot switch to " + newType + " — must complete the trial.");
            ApplyHeroStatBoost();
            return;
        }

        // Previously this called TransferMaxedWeaponStats(oldWeapon, newWeapon),
        // which auto-promoted the NEW weapon all the way to its damage cap if
        // the OLD weapon had been maxed. That meant swapping out a fully-
        // upgraded sword would hand the new bow / shield / whatever an
        // instant max-damage promotion, and every subsequent boost on it
        // would land in the post-max scaled-damage path instead of showing
        // fresh "+5 Damage" pickups. Disabled so a swap leaves the new
        // weapon at whatever progress it already had on its persistent
        // component (fresh if never equipped before, or the level it was
        // at the last time the player used it).

        if (replacePrimary)
        {
            primaryWeapon = newWeapon;
            Debug.Log("Primary weapon replaced with " + newWeapon.weaponName + ".");
        }
        else
        {
            secondaryWeapon = newWeapon;
            Debug.Log("Secondary weapon replaced with " + newWeapon.weaponName + ".");
        }

        speedMultiplier = 1f;
        RefreshWeaponVisuals();
    }

    private void TransferMaxedWeaponStats(Weapon oldWeapon, Weapon newWeapon)
    {
        if (oldWeapon == null || newWeapon == null)
            return;

        if (oldWeapon == newWeapon)
            return;

        if (!oldWeapon.IsDamageMaxed)
            return;

        while (!newWeapon.IsDamageMaxed)
        {
            if (!newWeapon.TryUpgradeDamage())
                break;
        }

        Debug.Log("Transferred max weapon upgrade from " + oldWeapon.weaponName + " to " + newWeapon.weaponName + ".");
    }

    private bool ShouldReplacePrimaryByDefault(eWeaponType newType)
    {
        switch (newType)
        {
            case eWeaponType.sword:
            case eWeaponType.dagger:
                return true;

            case eWeaponType.shield:
            case eWeaponType.bow:
            case eWeaponType.crossbow:
            case eWeaponType.grenade:
                return false;
        }

        return false;
    }

    public Weapon GetWeaponComponentForType(eWeaponType type)
    {
        switch (type)
        {
            case eWeaponType.sword:
                return swordWeapon;

            case eWeaponType.shield:
                return shieldWeapon;

            case eWeaponType.bow:
                return bowWeapon;

            case eWeaponType.dagger:
                return daggerWeapon;

            case eWeaponType.crossbow:
                return crossbowWeapon;

            case eWeaponType.grenade:
                return grenadeWeapon;

            default:
                return null;
        }
    }

    /// <summary>
    /// Equip the weapon component for <paramref name="type"/> into a specific
    /// slot. <paramref name="slotIndex"/> is 0 for primary, 1 for secondary.
    /// Used by the powerup choice UI when a slot is empty.
    /// </summary>
    public void EquipWeaponInSlot(int slotIndex, eWeaponType type)
    {
        Weapon w = GetWeaponComponentForType(type);
        if (w == null)
        {
            Debug.LogWarning("Hero: no weapon component exists for " + type + ". Taking stat boost instead.");
            ApplyHeroStatBoost();
            return;
        }

        // Zenith curse: only swords are allowed to be equipped.
        if (IsZenithCursed && type != eWeaponType.sword)
        {
            Debug.Log("[Zenith Curse] Cannot equip " + type + " — must complete the trial.");
            ApplyHeroStatBoost();
            return;
        }

        if (slotIndex == 0) primaryWeapon = w;
        else                secondaryWeapon = w;

        speedMultiplier = 1f;
        RefreshWeaponVisuals();
    }

    /// <summary>
    /// Apply a pickup-defined boost (Damage, Projectiles, Range, AttackSpeed)
    /// to a specific equipped weapon. Used by the powerup choice UI's Boost
    /// button. Falls back to a Hero stat boost if the weapon is already at
    /// the cap for that kind.
    /// </summary>
    public void UpgradeWeaponPower(Weapon weapon, BoostKind kind)
    {
        if (weapon == null) { ApplyHeroStatBoost(); return; }
        if (!weapon.TryApplyBoost(kind))
        {
            Debug.Log(weapon.weaponName + " is maxed for " + kind + ". Taking Hero stat boost instead.");
            ApplyHeroStatBoost();
        }
        else
        {
            Debug.Log("Boosted " + weapon.weaponName + ": " + weapon.DescribeBoost(kind));
        }
    }

    /// <summary>
    /// Picks a non-Random hero stat to boost using the existing
    /// maxedWeaponBoostMode setting. If that's Random, picks from the full
    /// stat pool at random, excluding any stat that's already at its cap.
    /// </summary>
    public HeroStatBoostMode RollHeroStatBoost()
    {
        if (maxedWeaponBoostMode != HeroStatBoostMode.Random)
            return maxedWeaponBoostMode;

        var candidates = new System.Collections.Generic.List<HeroStatBoostMode>();
        candidates.Add(HeroStatBoostMode.MaxHP); // MaxHP has no hard cap.
        // MoveSpeed grants both +speed and -dash cooldown; either having room is enough.
        bool speedRoom = moveSpeed    < maxMoveSpeed   - 0.001f;
        bool dashRoom  = dashCooldown > minDashCooldown + 0.001f;
        if (speedRoom || dashRoom) candidates.Add(HeroStatBoostMode.MoveSpeed);
        if (healthRegenPerSecond  < maxHealthRegenPerSecond - 0.001f)  candidates.Add(HeroStatBoostMode.HealthRegen);
        if (damageMultiplier      < maxDamageMultiplier - 0.001f)      candidates.Add(HeroStatBoostMode.DamageBoost);
        if (critRate              < maxCritRate - 0.001f)              candidates.Add(HeroStatBoostMode.CritRate);
        if (critDamage            < maxCritDamage - 0.001f)            candidates.Add(HeroStatBoostMode.CritDamage);
        return candidates[Random.Range(0, candidates.Count)];
    }

    /// <summary>Short label describing what <paramref name="stat"/> would do if applied right now.</summary>
    public string DescribeHeroStatBoost(HeroStatBoostMode stat)
    {
        switch (stat)
        {
            case HeroStatBoostMode.MaxHP:
                if (iAmTankActive)
                {
                    // Tank: ×(1 + iAmTankHpExponentialPerBoost) per pickup.
                    // Show both percent and the actual HP gained at the
                    // current MaxHP so the player can see the live impact.
                    float pct = Mathf.Max(0f, iAmTankHpExponentialPerBoost) * 100f;
                    float gain = maxHP * Mathf.Max(0f, iAmTankHpExponentialPerBoost);
                    return $"+{pct:0.#}% Max HP  (+{gain:0.#})";
                }
                return $"+{maxHPBoostAmount:0.#} Max HP";

            case HeroStatBoostMode.MoveSpeed:
                {
                    bool speedFull = moveSpeed >= maxMoveSpeed - 0.001f;
                    bool dashFull  = dashCooldown <= minDashCooldown + 0.001f;
                    if (speedFull && dashFull) return "Move Speed Maxed";

                    string speedPart = speedFull
                        ? "Speed maxed"
                        : $"+{Mathf.Min(maxMoveSpeed, moveSpeed + moveSpeedBoostAmount) - moveSpeed:0.##} Move Speed";
                    string dashPart = dashFull
                        ? "Dash maxed"
                        : $"-{dashCooldownReductionPercent * 100f:0}% Dash Cooldown";
                    return speedPart + "  •  " + dashPart;
                }

            case HeroStatBoostMode.HealthRegen:
                if (healthRegenPerSecond >= maxHealthRegenPerSecond - 0.001f) return "Regen Maxed";
                {
                    // Tank: regen pickups gain ×iAmTankRegenMultiplier (defaults
                    // to 2). Show the actual delta the player will get.
                    float perBoost = healthRegenBoostAmount;
                    if (iAmTankActive) perBoost *= Mathf.Max(0f, iAmTankRegenMultiplier);
                    float regenDelta = Mathf.Min(maxHealthRegenPerSecond, healthRegenPerSecond + perBoost) - healthRegenPerSecond;
                    return $"+{regenDelta:0.##} HP/sec";
                }

            case HeroStatBoostMode.DamageBoost:
                if (damageMultiplier >= maxDamageMultiplier - 0.001f) return "Damage Maxed";
                float dmgDelta = Mathf.Min(maxDamageMultiplier, damageMultiplier + damageBoostAmount) - damageMultiplier;
                return $"+{dmgDelta * 100f:0}% Damage";

            case HeroStatBoostMode.CritRate:
                if (critRate >= maxCritRate - 0.001f) return "Crit Rate Maxed";
                float crDelta = Mathf.Min(maxCritRate, critRate + critRateBoostAmount) - critRate;
                return $"+{crDelta * 100f:0}% Crit Rate";

            case HeroStatBoostMode.CritDamage:
                if (critDamage >= maxCritDamage - 0.001f) return "Crit Damage Maxed";
                float cdDelta = Mathf.Min(maxCritDamage, critDamage + critDamageBoostAmount) - critDamage;
                return $"+{cdDelta:0.##}× Crit Damage";

            default:
                return "+Stat";
        }
    }

    /// <summary>Apply a specific hero stat boost (no random roll). Used by the UI when a maxed weapon's boost is converted.</summary>
    public void ApplyHeroStatBoost(HeroStatBoostMode stat)
    {
        // While the Zenith curse is active, every powerup applies at HALF
        // efficiency — and the missing half is BANKED per-stat so when the
        // curse lifts (RefundZenithCurseGains), the player retroactively
        // gets the full-strength version of every powerup taken during
        // the trial.
        bool cursed = IsZenithCursed;
        float effEff = cursed ? 0.5f : 1f;     // efficiency multiplier
        float refundEff = cursed ? 0.5f : 0f;  // amount to bank for refund

        switch (stat)
        {
            case HeroStatBoostMode.MaxHP:
                if (iAmTankActive)
                {
                    // Exponential scaling: each pickup multiplies MaxHP by
                    // (1 + iAmTankHpExponentialPerBoost × eff).
                    float fullPct = Mathf.Max(0f, iAmTankHpExponentialPerBoost);
                    float effPct  = fullPct * effEff;
                    float oldMax  = maxHP;
                    maxHP        *= (1f + effPct);
                    currentHP   += (maxHP - oldMax);
                    if (currentHP > maxHP) currentHP = maxHP;
                    // Bank the missing flat-equivalent gain at refund time.
                    pendingHPRefund += oldMax * fullPct * refundEff;
                    Debug.Log($"Hero max HP scaled ×{1f + effPct:0.000} (Tank{(cursed ? ", cursed" : "")}): {oldMax:0.#} → {maxHP:0.#}.");
                }
                else
                {
                    float gain   = maxHPBoostAmount * effEff;
                    float refund = maxHPBoostAmount * refundEff;
                    maxHP     += gain;
                    currentHP += gain;
                    pendingHPRefund += refund;
                    Debug.Log($"Hero max HP +{gain:0.#}{(cursed ? " (cursed)" : "")} → {maxHP}.");
                }
                break;

            case HeroStatBoostMode.MoveSpeed:
                {
                    float speedGain   = moveSpeedBoostAmount * effEff;
                    float speedRefund = moveSpeedBoostAmount * refundEff;
                    float prev = moveSpeed;
                    moveSpeed = Mathf.Min(maxMoveSpeed, moveSpeed + speedGain);
                    pendingMoveSpeedRefund += speedRefund;
                    if (dashCooldownReductionPercent > 0f)
                    {
                        // Curse: cooldown reduction is also halved. Refund
                        // tracks the EXTRA reduction we'd have applied
                        // (delta between full and half) so the refund pass
                        // can knock more off later.
                        float pct       = dashCooldownReductionPercent;
                        float effPct    = pct * effEff;
                        float oldCD     = dashCooldown;
                        dashCooldown    = Mathf.Max(minDashCooldown, dashCooldown / (1f + effPct));
                        // Hypothetical full-effect new cooldown for refund accounting.
                        float fullCD    = Mathf.Max(minDashCooldown, oldCD / (1f + pct));
                        pendingDashCooldownRefund += Mathf.Max(0f, dashCooldown - fullCD);
                        if (dashCooldown < oldCD - 0.0001f && oldCD > 0f)
                            dashIFrameMultiplier *= dashCooldown / oldCD;
                    }
                    Debug.Log($"Hero move speed → {moveSpeed}, dash cooldown → {dashCooldown}, i-frame ×{dashIFrameMultiplier:F2}{(cursed ? " (cursed)" : "")}");
                }
                break;

            case HeroStatBoostMode.HealthRegen:
                {
                    float gainBase = healthRegenBoostAmount;
                    if (iAmTankActive) gainBase *= Mathf.Max(0f, iAmTankRegenMultiplier);
                    float gain   = gainBase * effEff;
                    float refund = gainBase * refundEff;
                    healthRegenPerSecond = Mathf.Min(maxHealthRegenPerSecond,
                        healthRegenPerSecond + gain);
                    pendingRegenRefund += refund;
                    Debug.Log($"Hero health regen +{gain:0.##}{(cursed ? " (cursed)" : "")} → {healthRegenPerSecond} HP/sec.");
                }
                break;

            case HeroStatBoostMode.DamageBoost:
                {
                    float gain   = damageBoostAmount * effEff;
                    float refund = damageBoostAmount * refundEff;
                    damageMultiplier = Mathf.Min(maxDamageMultiplier, damageMultiplier + gain);
                    pendingDamageMultRefund += refund;
                    Debug.Log($"Hero damage multiplier +{gain:0.##}{(cursed ? " (cursed)" : "")} → {damageMultiplier}×.");
                }
                break;

            case HeroStatBoostMode.CritRate:
                {
                    float gain   = critRateBoostAmount * effEff;
                    float refund = critRateBoostAmount * refundEff;
                    critRate = Mathf.Min(maxCritRate, critRate + gain);
                    pendingCritRateRefund += refund;
                    Debug.Log($"Hero crit rate +{gain * 100f:0.#}%{(cursed ? " (cursed)" : "")} → {critRate * 100f}%.");
                }
                break;

            case HeroStatBoostMode.CritDamage:
                {
                    float gain   = critDamageBoostAmount * effEff;
                    float refund = critDamageBoostAmount * refundEff;
                    critDamage = Mathf.Min(maxCritDamage, critDamage + gain);
                    pendingCritDamageRefund += refund;
                    Debug.Log($"Hero crit damage +{gain:0.##}×{(cursed ? " (cursed)" : "")} → {critDamage}×.");
                }
                break;
        }
    }

    /// <summary>
    /// Drain every pending Zenith-curse refund into the live stats so all
    /// powerups taken DURING the curse retroactively read as 100%-effective.
    /// Called by SwordWeapon.ApplyZenith the instant the curse lifts.
    /// </summary>
    public void RefundZenithCurseGains()
    {
        if (pendingHPRefund > 0f)
        {
            maxHP     += pendingHPRefund;
            currentHP += pendingHPRefund;
            if (currentHP > maxHP) currentHP = maxHP;
        }
        if (pendingMoveSpeedRefund > 0f)
            moveSpeed = Mathf.Min(maxMoveSpeed, moveSpeed + pendingMoveSpeedRefund);
        if (pendingDashCooldownRefund > 0f)
        {
            float oldCd = dashCooldown;
            dashCooldown = Mathf.Max(minDashCooldown, dashCooldown - pendingDashCooldownRefund);
            if (dashCooldown < oldCd - 0.0001f && oldCd > 0f)
                dashIFrameMultiplier *= dashCooldown / oldCd;
        }
        if (pendingRegenRefund > 0f)
            healthRegenPerSecond = Mathf.Min(maxHealthRegenPerSecond,
                healthRegenPerSecond + pendingRegenRefund);
        if (pendingDamageMultRefund > 0f)
            damageMultiplier = Mathf.Min(maxDamageMultiplier, damageMultiplier + pendingDamageMultRefund);
        if (pendingCritRateRefund > 0f)
            critRate = Mathf.Min(maxCritRate, critRate + pendingCritRateRefund);
        if (pendingCritDamageRefund > 0f)
            critDamage = Mathf.Min(maxCritDamage, critDamage + pendingCritDamageRefund);

        Debug.Log("[Zenith] Curse lifted — refunded pending stat gains: " +
                  $"HP+{pendingHPRefund:0.#}, Spd+{pendingMoveSpeedRefund:0.##}, " +
                  $"DashCdRef+{pendingDashCooldownRefund:0.##}, " +
                  $"Regen+{pendingRegenRefund:0.##}, Dmg+{pendingDamageMultRefund:0.##}, " +
                  $"Crit+{pendingCritRateRefund * 100f:0.#}%, CritDmg+{pendingCritDamageRefund:0.##}×.");

        pendingHPRefund = 0f;
        pendingMoveSpeedRefund = 0f;
        pendingDashCooldownRefund = 0f;
        pendingRegenRefund = 0f;
        pendingDamageMultRefund = 0f;
        pendingCritRateRefund = 0f;
        pendingCritDamageRefund = 0f;
    }

    /// <summary>
    /// Apply Hero damage modifiers to a base damage value: multiplies by
    /// damageMultiplier and rolls a critRate-chance critical hit
    /// (multiplying again by critDamage on success). Each weapon's Fire
    /// calls this once per attack so all hits in that attack consistently
    /// crit (or don't).
    /// </summary>
    public float ComputeAttackDamage(float baseDamage)
    {
        return ComputeAttackDamageWithCrit(baseDamage, out _);
    }

    /// <summary>
    /// Same logic as <see cref="ComputeAttackDamage"/> but exposes whether
    /// the crit roll succeeded. Used by ShieldWeapon (Shield Meteor augment)
    /// to gate a 50% meteor-spawn chance on crit hits without a duplicate
    /// crit roll. <paramref name="wasCrit"/> is set BEFORE the I-am-Tank /
    /// Zenith-curse multipliers are applied, so it reflects the raw crit
    /// outcome irrespective of late-stage damage modifiers.
    /// </summary>
    public float ComputeAttackDamageWithCrit(float baseDamage, out bool wasCrit)
    {
        wasCrit = false;
        float dmg = baseDamage * Mathf.Max(0f, damageMultiplier);
        if (critRate > 0f && Random.value < critRate)
        {
            dmg *= Mathf.Max(1f, critDamage);
            wasCrit = true;
        }
        if (IsTankShieldActive)
            dmg *= Mathf.Max(0f, iAmTankDamageMultiplier);
        if (IsZenithCursed)
            dmg *= 0.25f;
        return dmg;
    }

    /// <summary>
    /// Same multipliers as <see cref="ComputeAttackDamage"/> but skips the
    /// crit roll. Used for friendly-fire damage (e.g. the hero standing in
    /// their own grenade's AOE) so a player crit doesn't amplify self-damage.
    /// All other modifiers — damageMultiplier, the I-am-Tank shield buff,
    /// the Zenith curse — DO still apply, since those are general "your
    /// attacks are stronger / weaker" multipliers and friendly fire should
    /// reflect that consistently.
    /// </summary>
    public float ComputeAttackDamageNoCrit(float baseDamage)
    {
        float dmg = baseDamage * Mathf.Max(0f, damageMultiplier);
        if (IsTankShieldActive)
            dmg *= Mathf.Max(0f, iAmTankDamageMultiplier);
        if (IsZenithCursed)
            dmg *= 0.25f;
        return dmg;
    }

    /// <summary>
    /// Meteor general augment hook. Each weapon's Fire calls this after
    /// spawning its projectile/swing — if the augment is active, the shot
    /// was a crit, and the per-fire chance roll passes, a
    /// <see cref="MeteorArmer"/> is attached to the projectile so it can
    /// spawn a meteor on its first enemy hit.
    ///
    /// <paramref name="baseDamage"/> is the post-crit damage figure for this
    /// attack — so the meteor's damage scales with the player's crit damage
    /// stat and with that fire-event's actual hit damage. Pass the SAME
    /// damage value the projectile is dealing.
    ///
    /// <paramref name="enemyLayers"/> is the layer mask the meteor's AOE
    /// pass uses; pass the calling weapon's enemyLayers so the meteor only
    /// hits the same population the weapon does.
    /// </summary>
    public void TryArmMeteorOnProjectile(GameObject projectile, float baseDamage, bool wasCrit, LayerMask enemyLayers)
    {
        if (!meteorEnabled || !wasCrit || projectile == null) return;
        if (Random.value >= meteorChanceOnCrit) return;
        var armer = projectile.GetComponent<MeteorArmer>();
        if (armer == null) armer = projectile.AddComponent<MeteorArmer>();
        armer.Arm(
            damage:            baseDamage * Mathf.Max(0f, meteorDamageMultiplier),
            fallDuration:      meteorFallDuration,
            aoeRadius:         meteorAOERadius,
            vfxPrefab:         meteorVfxPrefab,
            vfxRotationOffset: meteorVfxRotationOffset,
            hitLayers:         enemyLayers);
    }

    /// <summary>Backward-compatible no-arg overload: rolls a random stat (respecting maxedWeaponBoostMode).</summary>
    public void ApplyHeroStatBoost()
    {
        ApplyHeroStatBoost(RollHeroStatBoost());
    }

    private void RefreshWeaponVisuals()
    {
        SetVisualActive(swordHandVisual, IsWeaponActive(eWeaponType.sword));
        SetVisualActive(shieldHandVisual, IsWeaponActive(eWeaponType.shield));
        SetVisualActive(bowHandVisual, IsWeaponActive(eWeaponType.bow));
        SetVisualActive(daggerHandVisual, IsWeaponActive(eWeaponType.dagger));
        SetVisualActive(crossbowHandVisual, IsWeaponActive(eWeaponType.crossbow));
        SetVisualActive(grenadeHandVisual, IsWeaponActive(eWeaponType.grenade));
    }

    private bool IsWeaponActive(eWeaponType type)
    {
        return (primaryWeapon != null && primaryWeapon.weaponType == type)
            || (secondaryWeapon != null && secondaryWeapon.weaponType == type);
    }

    private void SetVisualActive(GameObject visual, bool active)
    {
        if (visual != null && visual.activeSelf != active)
            visual.SetActive(active);
    }

    private void OnTriggerEnter(Collider other)
    {
        PowerUp powerUp = other.GetComponentInParent<PowerUp>();

        if (powerUp != null)
            powerUp.Collect(this);
    }

    /// <summary>
    /// Frozen-but-not-dead state set by <see cref="TriggerVictory"/>. While
    /// true the hero ignores input/movement and isn't damageable, but unlike
    /// IsDead it can be cleared by <see cref="ResumeFromVictoryPause"/> so
    /// the WinMenu's Continue button can put the player back in the run.
    /// </summary>
    public bool IsFrozenForVictory { get; private set; }

    /// <summary>
    /// Triggered by GameManager.OnFinalBossKilled. Freezes the hero (input
    /// off, no damage), waits <see cref="victoryEndSequenceDelay"/> seconds
    /// so the boss kill reads on screen, then opens the in-game WinMenu
    /// (which pauses Time.timeScale until the player picks a button).
    /// </summary>
    public void TriggerVictory()
    {
        if (IsFrozenForVictory || IsDead) return;
        IsFrozenForVictory = true;
        moveInput = Vector3.zero;
        speedMultiplier = 1f;
        if (rb != null) rb.velocity = Vector3.zero;
        if (animator != null) animator.SetFloat(kSpeed, 0f);

        // Pause AFTER the kill before opening the menu so the boss death
        // gets a beat on screen.
        Invoke(nameof(OpenWinMenu), Mathf.Max(0f, victoryEndSequenceDelay));
    }

    private void OpenWinMenu()
    {
        if (WinMenu.Instance != null) WinMenu.Instance.Show();
        else
        {
            // Fallback if no WinMenu was placed in the scene: behave like
            // the old flow and load the victory scene after the camera zoom.
            Debug.LogWarning("[Hero] No WinMenu in scene; falling back to victory-scene load.");
            BeginVictoryEndSequence();
        }
    }

    /// <summary>
    /// Called by the WinMenu's Continue button. Clears the frozen state so
    /// input/movement come back online and the hero can take damage again.
    /// </summary>
    public void ResumeFromVictoryPause()
    {
        IsFrozenForVictory = false;
        // Cancel the OpenWinMenu invoke just in case Continue is somehow
        // pressed before the delay elapsed (shouldn't happen but safe).
        CancelInvoke(nameof(OpenWinMenu));
    }

    private void BeginVictoryEndSequence()
    {
        if (Camera.main != null)
        {
            var follow = Camera.main.GetComponent<CameraFollow>();
            if (follow != null) follow.EnterDeathCam(transform);
        }
        Invoke(nameof(LoadVictoryScene), Mathf.Max(0f, endSequenceCameraTime));
    }

    private void Die()
    {
        if (IsDead) return;

        IsDead = true;
        moveInput = Vector3.zero;
        speedMultiplier = 1f;

        // Clean up the tank shield visual on death so it doesn't sit there
        // glowing on the corpse.
        if (tankShieldInstance != null) BreakTankShield();

        if (rb != null)
            rb.velocity = Vector3.zero;

        if (animator != null)
        {
            animator.SetFloat(kSpeed, 0f);
            animator.SetTrigger(kDie);
        }

        // Notify any subscribers (e.g. EnemyAnimator on every alive enemy → play victory clip).
        OnHeroDied?.Invoke();

        // Hand control to the death cam: focus + zoom on the corpse, ignore
        // cursor lean and screen shake until the EndScreen loads.
        if (Camera.main != null)
        {
            var follow = Camera.main.GetComponent<CameraFollow>();
            if (follow != null) follow.EnterDeathCam(transform);
        }

        // Match the deathcam zoom duration so the camera reaches its
        // final framed position before the EndScreen loads.
        Invoke(nameof(LoadGameOver), Mathf.Max(0f, endSequenceCameraTime));
    }

    private void LoadGameOver()
    {
        SceneManager.LoadScene(string.IsNullOrEmpty(gameOverSceneName) ? "EndScreen" : gameOverSceneName);
    }

    private void LoadVictoryScene()
    {
        SceneManager.LoadScene(string.IsNullOrEmpty(victorySceneName) ? "VictoryScreen" : victorySceneName);
    }
}