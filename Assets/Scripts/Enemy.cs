using UnityEngine;
using UnityEngine.UI;

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
    public enum Behavior { Chaser, Charger, Tank, Ranged, Crossbow, SlimeKing, SlimeGod }

    [Header("Behavior")]
    public Behavior behavior = Behavior.Chaser;

    [Header("Stats")]
    [Tooltip("Maximum hit points. Stored as long so very large scaled values keep integer precision (a float starts losing accuracy past ~16M, which matters once boss HP scaling stacks).")]
    public long maxHP = 30L;
    public float attackDamage = 8f;
    public float moveSpeed = 3.5f;

    /// <summary>
    /// Runtime debuff applied by the Dagger's Elemental Shiv augment.
    /// While shivDebuffEndTime is in the future, EffectiveMoveSpeed and
    /// EffectiveAttackDamage scale moveSpeed / attackDamage by these
    /// factors. Refresh-on-rehit semantics: every shiv hit resets
    /// shivDebuffEndTime to now+duration but never stacks the factors.
    /// </summary>
    [System.NonSerialized] public float shivSlowFactor = 1f;
    [System.NonSerialized] public float shivDamageFactor = 1f;
    [System.NonSerialized] public float shivDebuffEndTime = -1f;
    /// <summary>True while a shiv debuff is active on this enemy.</summary>
    public bool IsShivDebuffActive => shivDebuffEndTime > 0f && Time.time < shivDebuffEndTime;
    /// <summary>moveSpeed scaled by any active runtime debuffs.</summary>
    public float EffectiveMoveSpeed => moveSpeed * (IsShivDebuffActive ? shivSlowFactor : 1f);
    /// <summary>attackDamage scaled by any active runtime debuffs.</summary>
    public float EffectiveAttackDamage => attackDamage * (IsShivDebuffActive ? shivDamageFactor : 1f);

    /// <summary>
    /// Apply (or refresh) the Elemental Shiv debuff on this enemy. Called
    /// by ElementalShivClone when its strike lands. Refresh semantics
    /// always reset duration; the slow / damage factors come from the
    /// dagger config and aren't multiplied across re-hits.
    /// </summary>
    public void ApplyShivDebuff(float duration, float slowFactor, float damageFactor)
    {
        if (IsDead || duration <= 0f) return;
        shivSlowFactor   = Mathf.Clamp01(slowFactor);
        shivDamageFactor = Mathf.Clamp01(damageFactor);
        shivDebuffEndTime = Time.time + duration;
    }

    [Header("Melee")]
    [Tooltip("Distance from the player at which a melee attack lands. Ignored by Ranged.")]
    public float attackRange = 1.4f;
    [Tooltip("Seconds between melee hits.")]
    public float attackCooldown = 1.0f;

    [Header("Detection")]
    [Tooltip("Maximum distance at which the enemy notices the player. 0 = always.")]
    public float aggroRange = 25f;

    [Header("Health Bar")]
    [Tooltip("Show a small world-space health bar above this enemy (and a bigger one with HP text on bosses).")]
    public bool showHealthBar = true;
    [Tooltip("Height above the enemy's pivot where the health bar sits (world units).")]
    public float healthBarHeight = 2.2f;
    [Tooltip("Size of the regular-enemy health bar in world units (width × height).")]
    public Vector2 healthBarSize = new Vector2(0.9f, 0.12f);
    [Tooltip("Size of the boss health bar in world units (width × height). Used when behavior = SlimeKing.")]
    public Vector2 bossHealthBarSize = new Vector2(2.6f, 0.32f);
    [Tooltip("Show numeric current/max HP inside the health bar. Recommended for bosses, off for regulars.")]
    public bool bossShowHpText = true;
    [Tooltip("Color of the bar fill at full health.")]
    public Color healthBarColor = new Color(0.85f, 0.2f, 0.2f, 1f);
    [Tooltip("Color of the bar background.")]
    public Color healthBarBgColor = new Color(0f, 0f, 0f, 0.7f);

    [Header("Debug")]
    [Tooltip("If true, shows current HP as a small text label above the enemy via OnGUI. Off by default — the world-space health bar replaces it.")]
    public bool debugShowHealth = false;
    [Tooltip("How far above the enemy's pivot the OnGUI debug label sits.")]
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

    [Tooltip("Y position the powerup root spawns at, in world space. Assumes the floor is at Y=0; the X/Z come from the enemy's position so the drop lands where it died. Keep small (0–0.25) so the player can walk up to it.")]
    public float powerUpDropHeight = 0f;

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
    [Tooltip("Seconds INTO the telegraph that the dash direction is locked in. Before this point the charger continues tracking the player's current position; after this point the direction is committed and the charger will dash at that locked spot even if the player moves. The remaining (dashTelegraphTime - dashLockOnDelay) is the player's window to dodge. Set 0 = lock instantly at telegraph start (most dodgeable). Set >= dashTelegraphTime = lock at the very end (no dodge window — old behavior).")]
    public float dashLockOnDelay = 0.15f;
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

    private long currentHP;
    private float meleeTimer;
    private float shootTimer;
    private Rigidbody rb;
    private Hero player;
    private DamageFlash damageFlash;
    private EnemyAnimator enemyAnimator;

    // Optional final-boss controller. Awake on Enemy runs first; SlimeGod is
    // looked up lazily because RequireComponent can't enforce a nullable add-on.
    private SlimeGod slimeGod;
    private bool slimeGodLookedUp;

    private SlimeGod GetSlimeGod()
    {
        if (!slimeGodLookedUp)
        {
            slimeGod = GetComponent<SlimeGod>();
            slimeGodLookedUp = true;
        }
        return slimeGod;
    }

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
    /// <summary>
    /// Tracks how much of the lock-on window remains within the current
    /// telegraph. While > 0 the charger keeps re-aiming dashDirection at
    /// the player's live position; once it ticks to 0 the direction is
    /// frozen and the rest of the telegraph is the player's dodge window.
    /// </summary>
    private float dashLockOnTimer;

    public long CurrentHP => currentHP;
    public long MaxHP => maxHP;
    public bool IsDead { get; private set; }

    /// <summary>
    /// Multiply this enemy's max HP and current HP by <paramref name="multiplier"/>.
    /// Call right after Instantiate so the spawner can scale HP with elapsed
    /// game time. Awake() runs synchronously inside Instantiate and sets
    /// currentHP = maxHP, so multiplying both here keeps them in sync.
    /// HP is long but the multiplier is float; the result is rounded back to
    /// long, so very small multipliers may collapse to 0 — guard with a
    /// minimum of 1.
    /// </summary>
    public void ScaleHP(float multiplier)
    {
        if (multiplier <= 0f) return;
        // Use double precision intermediate so big-number boss HP stays
        // accurate when multiplied by exponential per-spawn factors.
        double scaledMax = (double)maxHP     * multiplier;
        double scaledCur = (double)currentHP * multiplier;
        // Clamp to long range and floor at 1 so an enemy can't spawn dead.
        maxHP     = (long)System.Math.Max(1.0, System.Math.Min(scaledMax, (double)long.MaxValue));
        currentHP = (long)System.Math.Max(1.0, System.Math.Min(scaledCur, (double)long.MaxValue));
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        damageFlash = GetComponent<DamageFlash>();
        enemyAnimator = GetComponent<EnemyAnimator>();
        if (enemyAnimator == null) enemyAnimator = GetComponentInChildren<EnemyAnimator>();
        currentHP = maxHP;

        // The final-boss controller (SlimeGod component) renders its own
        // telegraphs and replaces the world-space HP bar with a screen-bottom
        // bar built by GameHUD, so skip both setups for that behavior.
        if (behavior == Behavior.SlimeGod)
        {
            showHealthBar = false;
        }

        // Apply the difficulty preset to enemies whose stats it tunes.
        //   * Crossbow-behavior enemies (and the SlimeKing's ranged phase,
        //     which uses the same crossbowDamage field) scale by
        //     crossbowDamageMultiplier.
        //   * SlimeKings additionally take slimeKingAttackSpeedBoostMultiplier
        //     for the ranged-mode attack-speed boost.
        //   * SlimeGod (final boss) scales ALL of its damage moves through
        //     its own attackDamageMultiplier — set here from the preset.
        if (GameSettings.Instance != null)
        {
            var p = GameSettings.Instance.GetActivePreset();
            if (behavior == Behavior.Crossbow || behavior == Behavior.SlimeKing)
                crossbowDamage *= p.crossbowDamageMultiplier;
            if (behavior == Behavior.SlimeKing)
                skAttackSpeedBoostMultiplier = p.slimeKingAttackSpeedBoostMultiplier;
            if (behavior == Behavior.SlimeGod)
            {
                SlimeGod sg = GetSlimeGod();
                if (sg != null) sg.attackDamageMultiplier *= p.finalBossDamageMultiplier;
            }
        }

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

        if (showHealthBar) BuildHealthBar();
    }

    // -------------------- Health bar (world-space, billboarded) --------------------

    private Transform healthBarRoot;
    private Image healthBarFill;
    private Text healthBarText;
    private float healthBarMaxWidthPx;

    private void BuildHealthBar()
    {
        bool isBoss = behavior == Behavior.SlimeKing;
        Vector2 sizeWorld = isBoss ? bossHealthBarSize : healthBarSize;
        // Use a fixed-pixel canvas scaled down so 1 px = 0.01 world unit.
        // That keeps font rendering crisp regardless of how big the bar is.
        const float worldPerPixel = 0.01f;
        Vector2 sizePx = sizeWorld / worldPerPixel;

        GameObject canvasGo = new GameObject("HealthBar",
            typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = new Vector3(0f, healthBarHeight, 0f);
        canvasGo.transform.localScale = Vector3.one * worldPerPixel;
        canvasGo.layer = gameObject.layer;

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;

        var canvasRt = (RectTransform)canvasGo.transform;
        canvasRt.sizeDelta = sizePx;

        // Background panel
        GameObject bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        var bgImg = bgGo.GetComponent<Image>();
        bgImg.color = healthBarBgColor;
        bgImg.raycastTarget = false;

        // Foreground fill
        GameObject fillContainer = new GameObject("FillContainer", typeof(RectTransform));
        fillContainer.transform.SetParent(canvasGo.transform, false);
        var fcRt = (RectTransform)fillContainer.transform;
        // Inset by a small margin so the fill doesn't cover the BG outline.
        float insetPx = isBoss ? 4f : 2f;
        fcRt.anchorMin = Vector2.zero; fcRt.anchorMax = Vector2.one;
        fcRt.offsetMin = new Vector2(insetPx, insetPx);
        fcRt.offsetMax = new Vector2(-insetPx, -insetPx);

        // The Fill rect is anchored to the LEFT side of FillContainer with a
        // pivot on its own left edge, and we drive its width via sizeDelta.x.
        // This avoids Image.Type.Filled, which is a no-op without a sprite —
        // the original cause of the bar always reading full red.
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(fillContainer.transform, false);
        var fillRt = (RectTransform)fillGo.transform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);   // stretch vertically, anchor left
        fillRt.pivot     = new Vector2(0f, 0.5f); // grow rightward from the left edge
        fillRt.anchoredPosition = Vector2.zero;
        healthBarMaxWidthPx = sizePx.x - insetPx * 2f;
        fillRt.sizeDelta = new Vector2(healthBarMaxWidthPx, 0f);
        healthBarFill = fillGo.GetComponent<Image>();
        healthBarFill.color = healthBarColor;
        healthBarFill.type = Image.Type.Simple;
        healthBarFill.raycastTarget = false;

        // HP text (bosses only by default).
        if (isBoss && bossShowHpText)
        {
            GameObject txtGo = new GameObject("HPText", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(canvasGo.transform, false);
            var txtRt = (RectTransform)txtGo.transform;
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;

            healthBarText = txtGo.GetComponent<Text>();
            healthBarText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            healthBarText.alignment = TextAnchor.MiddleCenter;
            healthBarText.color = Color.white;
            healthBarText.fontSize = Mathf.Max(8, Mathf.RoundToInt(sizePx.y * 0.65f));
            healthBarText.horizontalOverflow = HorizontalWrapMode.Overflow;
            healthBarText.verticalOverflow = VerticalWrapMode.Overflow;
            healthBarText.raycastTarget = false;

            var outline = txtGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.4f, -1.4f);
        }

        healthBarRoot = canvasGo.transform;
    }

    private void LateUpdate()
    {
        if (healthBarRoot == null) return;

        // Hide bar once dead so it doesn't linger an extra frame.
        if (IsDead)
        {
            if (healthBarRoot.gameObject.activeSelf) healthBarRoot.gameObject.SetActive(false);
            return;
        }

        // Billboard: face the camera every frame so the bar reads correctly
        // regardless of the enemy's orientation.
        Camera cam = Camera.main;
        if (cam != null)
            healthBarRoot.rotation = cam.transform.rotation;

        // Update fill ratio by resizing the rect's width — robust, doesn't
        // require a sprite the way Image.Type.Filled does. Cast through
        // double so massive long HP values don't lose precision.
        if (healthBarFill != null)
        {
            float ratio = maxHP > 0L ? (float)Mathf.Clamp01((float)((double)currentHP / (double)maxHP)) : 0f;
            var rt = healthBarFill.rectTransform;
            Vector2 sd = rt.sizeDelta;
            sd.x = healthBarMaxWidthPx * ratio;
            rt.sizeDelta = sd;
        }

        // Update HP numbers (bosses only).
        if (healthBarText != null)
            healthBarText.text = $"{currentHP} / {maxHP}";
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
            case Behavior.SlimeGod:
                // The SlimeGod component drives its own movement and attack
                // logic (coroutines + transform writes). Enemy.cs just sits
                // here as the HP/damage/death surface.
                break;
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
                    // Track player for the first dashLockOnDelay seconds of
                    // the telegraph, then commit. Pre-seed dashDirection
                    // with the current aim so a zero-delay setup still has
                    // a sensible direction even if the lock-on tick fails
                    // to run on the same frame.
                    dashLockOnTimer = Mathf.Max(0f, dashLockOnDelay);
                    dashDirection = GetAvoidedDirection(dir);
                    StopMoving();
                }
                break;

            case ChargerPhase.Telegraph:
                StopMoving();
                // While the lock-on window is open, keep re-aiming at the
                // player's current position. Once it closes, the direction
                // is frozen and the rest of the telegraph (= dashTelegraphTime
                // - dashLockOnDelay) is the player's window to step out of
                // the line — which is what makes the dash actually dodgeable.
                if (dashLockOnTimer > 0f)
                {
                    dashLockOnTimer -= Time.fixedDeltaTime;
                    dashDirection = GetAvoidedDirection(dir);
                }
                if (chargerPhaseTimer <= 0f)
                {
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

        if (shootTimer <= 0f)
        {
            if (projectilePrefab != null)
            {
                Shoot(dir);
                shootTimer = shootCooldown;
            }
            else
            {
                Debug.LogWarning($"[{name}] TickRanged: shoot timer ready but Projectile Prefab is NULL — assign one in the Inspector.", this);
                shootTimer = shootCooldown; // throttle the warning so it doesn't spam every frame
            }
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
        if (enemyAnimator != null) enemyAnimator.OnAttack();
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
                if (enemyAnimator != null) enemyAnimator.OnAttack();
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
        // Keep the landing inside the arena. Without this clamp the slime can
        // land on top of (or past) the wall when the player kites the corner.
        if (ArenaGenerator.Instance != null)
            skJumpEnd = ArenaGenerator.Instance.ClampToArena(skJumpEnd);
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
        // Same clamp as JumpToMelee — retreat jumps near the wall would otherwise
        // launch the slime past it. Try the natural retreat first, then fall back
        // to the player's direction if the chosen target is already at the edge.
        if (ArenaGenerator.Instance != null)
        {
            Vector3 clamped = ArenaGenerator.Instance.ClampToArena(skJumpEnd);
            // If the clamp shortened the retreat to nothing (slime was already
            // jammed in the corner), instead jump along the wall toward the
            // player's perpendicular so it still moves.
            if ((clamped - transform.position).sqrMagnitude < 0.5f)
            {
                Vector3 perp = new Vector3(-awayFromPlayer.z, 0f, awayFromPlayer.x);
                clamped = ArenaGenerator.Instance.ClampToArena(transform.position + perp * skRetreatJumpDistance);
            }
            skJumpEnd = clamped;
        }
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

        // Apply runtime slow debuffs (Elemental Shiv) at the lowest movement
        // chokepoint so EVERY caller gets slowed without each AI path
        // having to remember the multiplier — chase, kite, charger dash,
        // SlimeKing melee speed, ranged retreat, etc.
        float effSpeed = speed * (IsShivDebuffActive ? shivSlowFactor : 1f);

        Vector3 v = dir * effSpeed;
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
        player.TakeDamage(EffectiveAttackDamage);
        meleeTimer = attackCooldown;
        if (enemyAnimator != null) enemyAnimator.OnAttack();
    }

    private void Shoot(Vector3 dir)
    {
        Vector3 spawnPos = projectileSpawn != null
            ? projectileSpawn.position
            : transform.position + transform.forward * 0.8f + Vector3.up * 1.0f;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        EnemyProjectile p = Instantiate(projectilePrefab, spawnPos, rot);
        p.Launch(dir, EffectiveAttackDamage);
        if (enemyAnimator != null) enemyAnimator.OnAttack();
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        // SlimeGod hook: damage reduction lerp + shield-stack absorber lives in
        // the SlimeGod component. Returns 0 if the hit was absorbed by a shield.
        if (behavior == Behavior.SlimeGod)
        {
            SlimeGod sg = GetSlimeGod();
            if (sg != null) amount = sg.ModifyIncomingDamage(amount);
            if (amount <= 0f) return;
        }

        if (skCurrentShieldHits > 0)
        {
            skCurrentShieldHits--;
            HitParticles.EmitBurst(transform.position + Vector3.up, Vector3.up,
                count: 10, speed: 5f, lifetime: 0.4f, size: 0.12f,
                color: new Color(0.4f, 0.8f, 1f), spreadAngle: 60f, useGravity: false);
            if (skCurrentShieldHits <= 0) DestroyShieldVisual();
            return;
        }

        // Convert float damage to a long delta. Round so fractional damage
        // (e.g. 1.5) doesn't truncate to 1 every hit; floor at 0 so a 0.4
        // hit still does nothing instead of negative.
        long longDamage = amount > 0f ? (long)System.Math.Max(0L, System.Math.Round((double)amount)) : 0L;
        currentHP = System.Math.Max(0L, currentHP - longDamage);
        if (damageFlash != null) damageFlash.Flash();
        if (enemyAnimator != null && currentHP > 0L) enemyAnimator.OnHit();
        SpawnHitParticles();
        if (currentHP <= 0L) Die();
    }

    /// <summary>
    /// Force this enemy to die NOW with no powerup drop and no boss-kill
    /// credit. Used by the EnemySpawner's final-boss shockwave to wipe every
    /// regular enemy off the arena when SlimeGod spawns.
    /// </summary>
    public void KillSilently(bool emitParticles = true)
    {
        if (IsDead) return;
        IsDead = true;
        currentHP = 0L;
        StopMoving();
        if (telegraphLine != null) telegraphLine.enabled = false;
        if (enemyAnimator != null) enemyAnimator.OnDie();
        if (emitParticles)
        {
            // Tiny burst so the wipe still reads on screen — but small enough
            // that 100 simultaneous KillSilently calls don't spawn 1200+
            // rigidbody-particle GameObjects in a single frame.
            HitParticles.EmitBurst(transform.position + Vector3.up * 0.6f, Vector3.up,
                count: 3, speed: 4f, lifetime: 0.35f, size: 0.14f,
                color: hitParticleColor, spreadAngle: 90f, useGravity: true);
        }
        Destroy(gameObject, 0.2f);
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
        if (enemyAnimator != null) enemyAnimator.OnDie();

        // Notify the SlimeGod controller BEFORE GameManager.OnEnemyKilled so
        // the controller can stop its coroutines and trigger the win flow
        // (which awards 1000 points instead of the regular boss credit).
        if (behavior == Behavior.SlimeGod)
        {
            SlimeGod sg = GetSlimeGod();
            if (sg != null) sg.OnBossKilled();
        }
        else if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnemyKilled(this);
        }

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

        // Scale the drop chance by the spawner's current multiplier so the
        // player gets generous drops early (when kills are rare) and fewer
        // drops per kill once spawn rates ramp up — net powerups per second
        // stays roughly steady throughout the run.
        float chance = powerUpDropChance;
        if (EnemySpawner.Instance != null)
            chance = Mathf.Clamp01(chance * EnemySpawner.Instance.GetCurrentDropChanceMultiplier());

        if (Random.value > chance) return;

        eWeaponType dropType = PickRandomPowerUpType();
        if (dropType == eWeaponType.none) return;

        // Take the enemy's XZ but force Y to powerUpDropHeight (assumes the
        // floor is at world Y=0). Simpler than raycasting — and combined with
        // PowerUp's XZ-only pickup distance, the player can walk up to and
        // grab the powerup regardless of any prefab-side visual offset.
        Vector3 spawnPos = new Vector3(transform.position.x, powerUpDropHeight, transform.position.z);

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

        string text = $"{currentHP} / {maxHP}";
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
        GUI.color = currentHP <= (long)(maxHP * 0.33) ? new Color(1f, 0.4f, 0.4f) : Color.white;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width, r.height), text);
        GUI.color = prev;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugShowHealth || !Application.isPlaying || IsDead) return;
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * debugLabelHeight,
            $"HP: {currentHP}/{maxHP}");
    }
#endif
}