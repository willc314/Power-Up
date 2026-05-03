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
        HealthRegen
    }

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
    [Tooltip("Current health regen, in HP per second. Boosts add to this.")]
    public float healthRegenPerSecond = 0f;
    [Tooltip("How much HP/sec regen increases per boost.")]
    public float healthRegenBoostAmount = 0.5f;
    [Tooltip("Cap on healthRegenPerSecond.")]
    public float maxHealthRegenPerSecond = 10f;

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

    private Rigidbody rb;
    private Camera cam;
    private Vector3 moveInput;
    private DamageFlash damageFlash;

    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;
    private float dashParticleAccumulator;
    private float invulnerabilityTimer;
    public bool IsDashing => dashTimer > 0f;

    /// <summary>
    /// True while the hero is invulnerable to damage. Currently set by the
    /// dash (covers the dash duration plus dashInvulnerabilityExtension).
    /// Other code paths (Hero.TakeDamage) early-out when this is true.
    /// </summary>
    public bool IsInvulnerable => dashInvulnerability && invulnerabilityTimer > 0f;

    private void Awake()
    {
        Instance = this;

        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        CacheWeaponComponents();
        ConfigureWeaponTypes();

        damageFlash = GetComponent<DamageFlash>();

        cam = Camera.main;
        currentHP = maxHP;

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
    }

    private void Update()
    {
        if (IsDead)
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

        // Passive health regen from hero stat boosts.
        if (healthRegenPerSecond > 0f && currentHP < maxHP)
            currentHP = Mathf.Min(maxHP, currentHP + healthRegenPerSecond * Time.deltaTime);

        if (animator != null)
        {
            float speed01 = moveInput.magnitude;
            animator.SetFloat(kSpeed, speed01, animSpeedDamping, Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (IsDead || Time.timeScale == 0f)
            return;

        if (IsDashing)
        {
            dashTimer -= Time.fixedDeltaTime;
            Vector3 step = dashDirection * dashSpeed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + step);
            EmitDashParticles();
        }
        else
        {
            // Zero residual velocity each tick so charger-style enemies (the
            // mushroom dash) don't keep shoving the hero across the floor
            // after the impact. Without this, the rigidbody picks up momentum
            // from the collision and slides for several frames before drag
            // brings it to rest.
            rb.velocity = Vector3.zero;

            Vector3 target = rb.position + moveInput * moveSpeed * speedMultiplier * Time.fixedDeltaTime;
            rb.MovePosition(target);
        }
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
        // Cover the whole dash plus the small grace period afterward.
        invulnerabilityTimer = dashDuration + Mathf.Max(0f, dashInvulnerabilityExtension);

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

    public void TakeDamage(float amount)
    {
        if (IsDead)
            return;

        // Dash i-frames: ignore damage while the invulnerability window is active.
        if (IsInvulnerable)
            return;

        currentHP = Mathf.Max(0f, currentHP - amount);

        if (damageFlash != null)
            damageFlash.Flash();

        if (currentHP <= 0f)
            Die();
    }

    public void Heal(float amount)
    {
        if (IsDead)
            return;

        currentHP = Mathf.Min(maxHP, currentHP + amount);
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

        TransferMaxedWeaponStats(oldWeapon, newWeapon);

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
    /// maxedWeaponBoostMode setting. If that's Random, picks one of MaxHP /
    /// MoveSpeed / HealthRegen at random, biasing away from any stat that's
    /// already at its cap.
    /// </summary>
    public HeroStatBoostMode RollHeroStatBoost()
    {
        if (maxedWeaponBoostMode != HeroStatBoostMode.Random)
            return maxedWeaponBoostMode;

        // Build the list of un-capped candidates, fall back to all-three if
        // somehow everything is capped (shouldn't happen in normal play).
        var candidates = new System.Collections.Generic.List<HeroStatBoostMode>();
        candidates.Add(HeroStatBoostMode.MaxHP); // MaxHP has no hard cap.
        if (moveSpeed           < maxMoveSpeed - 0.001f)         candidates.Add(HeroStatBoostMode.MoveSpeed);
        if (healthRegenPerSecond < maxHealthRegenPerSecond - 0.001f) candidates.Add(HeroStatBoostMode.HealthRegen);
        return candidates[Random.Range(0, candidates.Count)];
    }

    /// <summary>Short label describing what <paramref name="stat"/> would do if applied right now.</summary>
    public string DescribeHeroStatBoost(HeroStatBoostMode stat)
    {
        switch (stat)
        {
            case HeroStatBoostMode.MaxHP:
                return $"+{maxHPBoostAmount:0.#} Max HP";
            case HeroStatBoostMode.MoveSpeed:
                if (moveSpeed >= maxMoveSpeed - 0.001f) return "Move Speed Maxed";
                float speedDelta = Mathf.Min(maxMoveSpeed, moveSpeed + moveSpeedBoostAmount) - moveSpeed;
                return $"+{speedDelta:0.##} Move Speed";
            case HeroStatBoostMode.HealthRegen:
                if (healthRegenPerSecond >= maxHealthRegenPerSecond - 0.001f) return "Regen Maxed";
                float regenDelta = Mathf.Min(maxHealthRegenPerSecond, healthRegenPerSecond + healthRegenBoostAmount) - healthRegenPerSecond;
                return $"+{regenDelta:0.##} HP/sec";
            default:
                return "+Stat";
        }
    }

    /// <summary>Apply a specific hero stat boost (no random roll). Used by the UI when a maxed weapon's boost is converted.</summary>
    public void ApplyHeroStatBoost(HeroStatBoostMode stat)
    {
        switch (stat)
        {
            case HeroStatBoostMode.MaxHP:
                maxHP     += maxHPBoostAmount;
                currentHP += maxHPBoostAmount;
                Debug.Log("Hero max HP increased to " + maxHP + ".");
                break;

            case HeroStatBoostMode.MoveSpeed:
                moveSpeed = Mathf.Min(maxMoveSpeed, moveSpeed + moveSpeedBoostAmount);
                Debug.Log("Hero move speed increased to " + moveSpeed + ".");
                break;

            case HeroStatBoostMode.HealthRegen:
                healthRegenPerSecond = Mathf.Min(maxHealthRegenPerSecond,
                    healthRegenPerSecond + healthRegenBoostAmount);
                Debug.Log("Hero health regen increased to " + healthRegenPerSecond + " HP/sec.");
                break;
        }
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

    private void Die()
    {
        if (IsDead) return;

        IsDead = true;
        moveInput = Vector3.zero;
        speedMultiplier = 1f;

        if (rb != null)
            rb.velocity = Vector3.zero;

        if (animator != null)
        {
            animator.SetFloat(kSpeed, 0f);
            animator.SetTrigger(kDie);
        }

        // Hand control to the death cam: focus + zoom on the corpse, ignore
        // cursor lean and screen shake until the EndScreen loads.
        if (Camera.main != null)
        {
            var follow = Camera.main.GetComponent<CameraFollow>();
            if (follow != null) follow.EnterDeathCam(transform);
        }

        Invoke(nameof(LoadGameOver), 2f);
    }

    private void LoadGameOver()
    {
        SceneManager.LoadScene("EndScreen");
    }
}