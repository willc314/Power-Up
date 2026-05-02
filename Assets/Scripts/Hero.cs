using UnityEngine;

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
        MoveSpeed
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

    [Header("Active Weapons")]
    [Tooltip("Fired on left click. Drag a Weapon component from this Hero here.")]
    public Weapon primaryWeapon;

    [Tooltip("Fired on right click. Drag a Weapon component from this Hero here.")]
    public Weapon secondaryWeapon;

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

        DispatchWeaponInput(0, primaryWeapon);
        DispatchWeaponInput(1, secondaryWeapon);

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

        Vector3 target = rb.position + moveInput * moveSpeed * speedMultiplier * Time.fixedDeltaTime;
        rb.MovePosition(target);
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
        Weapon newWeapon = GetWeaponComponentForType(newType);

        if (newWeapon == null)
        {
            Debug.LogWarning("Hero: no weapon component exists for " + newType + ". Taking stat boost instead.");
            ApplyHeroStatBoost();
            return;
        }

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

    private Weapon GetWeaponComponentForType(eWeaponType type)
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

    public void ApplyHeroStatBoost()
    {
        HeroStatBoostMode boost = maxedWeaponBoostMode;

        if (boost == HeroStatBoostMode.Random)
        {
            boost = Random.value < 0.5f
                ? HeroStatBoostMode.MaxHP
                : HeroStatBoostMode.MoveSpeed;
        }

        switch (boost)
        {
            case HeroStatBoostMode.MaxHP:
                maxHP += maxHPBoostAmount;
                currentHP += maxHPBoostAmount;
                Debug.Log("Hero max HP increased to " + maxHP + ".");
                break;

            case HeroStatBoostMode.MoveSpeed:
                moveSpeed = Mathf.Min(maxMoveSpeed, moveSpeed + moveSpeedBoostAmount);
                Debug.Log("Hero move speed increased to " + moveSpeed + ".");
                break;
        }
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

        Debug.Log("Hero died.");
    }
}