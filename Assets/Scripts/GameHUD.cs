using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the in-game HUD entirely from script so you only need one component
/// in the scene to get a working overlay. Shows:
///   * Timer (top-left)         - reads from GameManager.Instance.ElapsedTime
///   * Score  (top-right)       - reads from GameManager.Instance.Score
///   * Health bar (bottom-left) - reads from Hero.Instance.CurrentHP / MaxHP
///   * Weapon slots (bottom-right) - one slot per weapon (primary + secondary),
///     each shows the weapon icon you assign, a cooldown overlay that drains
///     while the weapon is on cooldown, and the weapon's name.
///
/// Setup:
///   1. Create an empty GameObject named "GameHUD" in the scene.
///   2. Add this component.
///   3. Optionally drag sprites into Primary Weapon Icon / Secondary Weapon Icon.
///   4. Make sure there is a GameManager in the scene too (for the timer/score).
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("References (auto-found if left empty)")]
    public Hero hero;
    public GameManager gameManager;

    [Header("Weapon Icons (fallbacks)")]
    [Tooltip("Used in the Primary slot ONLY when the equipped weapon has no Hud Icon assigned. The HUD prefers Weapon.hudIcon when it's set, so each weapon shows its own icon automatically.")]
    public Sprite primaryWeaponIcon;
    [Tooltip("Used in the Secondary slot ONLY when the equipped weapon has no Hud Icon assigned.")]
    public Sprite secondaryWeaponIcon;

    [Header("Style")]
    public Color textColor = Color.white;
    public Color panelColor = new Color(0f, 0f, 0f, 0.45f);
    public Color outlineColor = new Color(1f, 1f, 1f, 0.35f);
    public Color healthColor = new Color(0.85f, 0.2f, 0.2f);
    public Color healthBackgroundColor = new Color(0f, 0f, 0f, 0.6f);
    public Color cooldownOverlayColor = new Color(0f, 0f, 0f, 0.6f);
    public Color bossBarColor = new Color(0.85f, 0.15f, 0.15f);
    public Color bossBarBgColor = new Color(0f, 0f, 0f, 0.75f);
    public int margin = 24;
    public int timerFontSize = 56;
    public int scoreFontSize = 36;
    public int hpFontSize = 22;
    public int weaponNameFontSize = 18;
    public int weaponSlotSize = 96;
    public int weaponSlotSpacing = 12;
    public int healthBarWidth = 320;
    public int healthBarHeight = 22;
    [Header("Boss Bar")]
    public int bossBarWidth = 1100;
    public int bossBarHeight = 36;
    public int bossBarFontSize = 24;
    public int bossBarBottomMargin = 28;

    [Header("Mouse-button labels")]
    public string primaryButtonLabel = "LMB";
    public string secondaryButtonLabel = "RMB";

    // ---- Built UI references ----
    private Canvas canvas;
    private Text timerText;
    private Text bossTimerText;
    private Image xpBarFill;
    private Text  xpBarLabel;
    private Text scoreText;
    private Text highScoreText;
    private Text newHighScoreText;
    private RectTransform healthFillRect;
    private Image healthFillImg;
    private Text healthText;
    private float healthBarMaxWidth;

    // Per-weapon slot widgets
    private struct Slot
    {
        public GameObject root;          // toggled off when no weapon is equipped
        public Image icon;
        public Image cooldownOverlay;
        public Text nameText;
    }
    private Slot primarySlot;
    private Slot secondarySlot;

    // Dash slot widgets (square next to the weapon slots).
    private struct DashWidgets
    {
        public GameObject root;
        public Image cooldownOverlay;
        public Text labelText;          // "DASH" when ready, "0.5" when on cooldown
    }
    private DashWidgets dashSlot;

    // Tank ability slot widgets (square ABOVE the dash slot). Hidden until
    // Hero.iAmTankActive flips true (player accepts the I-am-Tank upgrade).
    private struct TankWidgets
    {
        public GameObject root;
        public Image      cooldownOverlay;
        public Image      iconImage;     // optional sprite shown behind the label
        public Text       labelText;     // "TANK" when ready, "12.5" while on cooldown
    }
    private TankWidgets tankSlot;
    [Tooltip("Optional icon for the I-am-Tank shield ability HUD slot. Square sprite (e.g. shield icon). Leave null for text-only.")]
    public Sprite tankAbilityIcon;

    // Meteor Rain ability slot (same screen position as tankSlot — they're
    // mutex via the slot-3 lockout, only one is ever visible).
    private TankWidgets meteorSlot;
    [Tooltip("Optional icon for the Meteor Rain ability HUD slot. Square sprite (e.g. comet icon). Leave null for text-only.")]
    public Sprite meteorAbilityIcon;

    // Boss HP bar (bottom-center; only shown while a SlimeGod is alive).
    private GameObject bossBarRoot;
    private RectTransform bossBarFillRect;
    private Image bossBarFillImg;
    private Text bossBarText;
    private Text bossBarLabel;
    private float bossBarMaxWidth;

    // Cached default font.
    private Font defaultFont;

    private void Awake()
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
    }

    private void Start()
    {
        // Try to find references at start so designers don't have to wire them.
        if (hero == null) hero = Hero.Instance;
        if (gameManager == null) gameManager = GameManager.Instance;
    }

    private void Update()
    {
        // Late-bind in case the hero spawns after this HUD (ArenaGenerator
        // instantiates the hero from a prefab at runtime).
        if (hero == null) hero = Hero.Instance;
        if (gameManager == null) gameManager = GameManager.Instance;

        UpdateTimer();
        UpdateScore();
        UpdateHealth();
        UpdateWeaponSlots();
        UpdateDashSlot();
        UpdateTankSlot();
        UpdateMeteorSlot();
        UpdateBossBar();
        UpdateXPBar();
    }

    // -------------------- Per-frame updates --------------------

    private void UpdateTimer()
    {
        if (timerText == null) return;
        // Use EffectiveSurvivalTime so the displayed run-timer freezes the
        // moment the SlimeGod spawns and resumes once the boss dies (the
        // same value drives the survival-points score so HUD and score
        // stay in lockstep).
        float t = gameManager != null ? gameManager.EffectiveSurvivalTime : 0f;
        timerText.text = GameManager.FormatTime(t);

        // Boss sub-timer below the main timer — only visible while a
        // SlimeGod is alive.
        UpdateBossTimer();
    }

    private void UpdateBossTimer()
    {
        if (bossTimerText == null) return;
        SlimeGod boss = FindActiveFinalBoss();
        bool show = boss != null && boss.IsActive;
        if (bossTimerText.gameObject.activeSelf != show)
            bossTimerText.gameObject.SetActive(show);
        if (show) bossTimerText.text = "Boss " + GameManager.FormatTime(boss.LifeTime);
    }

    private void UpdateScore()
    {
        if (scoreText == null) return;
        int s  = gameManager != null ? gameManager.Score     : 0;
        int hs = gameManager != null ? gameManager.HighScore : 0;
        scoreText.text = "Score  " + s.ToString();

        if (highScoreText != null) highScoreText.text = "Best   " + hs.ToString();

        if (newHighScoreText != null && gameManager != null)
        {
            bool show = gameManager.NewHighScoreThisRun;
            if (newHighScoreText.gameObject.activeSelf != show)
                newHighScoreText.gameObject.SetActive(show);

            if (show)
            {
                // Gentle pulse so the player can't miss it.
                float pulse = 0.85f + 0.15f * (Mathf.Sin(Time.unscaledTime * 4.5f) * 0.5f + 0.5f);
                Color c = newHighScoreText.color;
                c.a = pulse;
                newHighScoreText.color = c;
            }
        }
    }

    private void UpdateHealth()
    {
        if (healthFillRect == null || healthText == null) return;
        // Wait quietly for the hero to spawn (the ArenaGenerator instantiates
        // it at runtime so it might not be there for the first frame).
        if (hero == null) return;

        float cur = hero.CurrentHP;
        float max = Mathf.Max(1f, hero.MaxHP);

        float ratio = Mathf.Clamp01(cur / max);
        Vector2 size = healthFillRect.sizeDelta;
        size.x = healthBarMaxWidth * ratio;
        healthFillRect.sizeDelta = size;

        healthText.text = Mathf.CeilToInt(cur) + " / " + Mathf.CeilToInt(max);
    }

    private void UpdateDashSlot()
    {
        if (dashSlot.labelText == null || hero == null) return;

        float remaining = hero.DashCooldownRemaining;
        float total = Mathf.Max(0.001f, hero.dashCooldown);

        if (remaining > 0.05f)
        {
            // On cooldown: show remaining seconds and drain the overlay.
            dashSlot.labelText.text = remaining.ToString("0.0");
            if (dashSlot.cooldownOverlay != null)
                dashSlot.cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / total);
        }
        else
        {
            // Ready: show the keybind label and clear the overlay.
            dashSlot.labelText.text = "DASH";
            if (dashSlot.cooldownOverlay != null) dashSlot.cooldownOverlay.fillAmount = 0f;
        }
    }

    private void UpdateWeaponSlots()
    {
        Weapon p = hero != null ? hero.primaryWeapon   : null;
        Weapon s = hero != null ? hero.secondaryWeapon : null;
        UpdateSlot(primarySlot,   p, primaryButtonLabel,   primaryWeaponIcon);
        UpdateSlot(secondarySlot, s, secondaryButtonLabel, secondaryWeaponIcon);
    }

    private void UpdateSlot(Slot slot, Weapon w, string buttonLabel, Sprite fallbackIcon)
    {
        if (slot.nameText == null) return;

        // No weapon in this slot → hide the whole slot so the HUD stays clean.
        if (w == null)
        {
            if (slot.root != null && slot.root.activeSelf) slot.root.SetActive(false);
            return;
        }

        if (slot.root != null && !slot.root.activeSelf) slot.root.SetActive(true);

        // Just the mouse-button label ("LMB" / "RMB"). The icon already
        // identifies which weapon is in the slot, so the name was redundant.
        slot.nameText.text = buttonLabel;

        // Prefer the icon the weapon itself carries so swapping/randomizing
        // weapons changes the HUD icon automatically. Fall back to the
        // slot-specific icon if the weapon hasn't been given one yet.
        Sprite icon = w.hudIcon != null ? w.hudIcon : fallbackIcon;

        if (slot.icon != null)
        {
            slot.icon.sprite = icon;
            slot.icon.color = icon != null
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(1f, 1f, 1f, 0.08f);
            // Per-weapon spin so we can hand-correct icons that came out
            // sideways from the renderer. Negate so positive values turn
            // clockwise from the user's point of view.
            slot.icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -w.hudIconRotation);
            // Per-weapon scale so each icon can be sized to fit the slot
            // regardless of how cropped the source sprite was.
            float zoom = w.hudIconZoom > 0f ? w.hudIconZoom : 1f;
            slot.icon.rectTransform.localScale = new Vector3(zoom, zoom, 1f);
        }

        if (slot.cooldownOverlay != null) slot.cooldownOverlay.fillAmount = w.CooldownProgress;
    }

    private static Enemy cachedBossEnemy;
    private static SlimeGod cachedBoss;

    private static SlimeGod FindActiveFinalBoss()
    {
        // Skip the FindObjectOfType scan during the first 8 minutes of a run
        // when no boss is alive yet — GameManager.IsFinalBossAlive flips true
        // the moment SlimeGod.OnEnable runs.
        if (GameManager.Instance == null || !GameManager.Instance.IsFinalBossAlive)
        {
            cachedBoss = null;
            cachedBossEnemy = null;
            return null;
        }
        if (cachedBoss == null || cachedBossEnemy == null)
        {
            cachedBoss = UnityEngine.Object.FindObjectOfType<SlimeGod>();
            cachedBossEnemy = cachedBoss != null ? cachedBoss.GetComponent<Enemy>() : null;
        }
        return cachedBoss;
    }

    private void UpdateBossBar()
    {
        if (bossBarRoot == null) return;

        SlimeGod boss = FindActiveFinalBoss();
        Enemy be = cachedBossEnemy;
        bool show = boss != null && be != null && !be.IsDead;

        if (bossBarRoot.activeSelf != show) bossBarRoot.SetActive(show);
        if (!show) return;

        long cur = be.CurrentHP;
        long max = System.Math.Max(1L, be.MaxHP);
        // Compute ratio as float through double so very large long HP values
        // don't lose precision when converted directly to float.
        float ratio = Mathf.Clamp01((float)((double)cur / (double)max));

        if (bossBarFillRect != null)
        {
            Vector2 sd = bossBarFillRect.sizeDelta;
            sd.x = bossBarMaxWidth * ratio;
            bossBarFillRect.sizeDelta = sd;
        }

        if (bossBarText != null)
            bossBarText.text = cur + " / " + max;

        if (bossBarLabel != null)
        {
            int dr = Mathf.RoundToInt(boss.CurrentDamageReduction * 100f);
            string shieldStr = boss.ShieldStacks > 0 ? "  •  Shield ×" + boss.ShieldStacks : "";
            // Rage phase: show ENRAGED ×N where N is the number of times the
            // boss has triggered the rage channel. Rendered orange to match
            // the channel particles.
            string rageStr = boss.ragePhaseTriggerCount > 0
                ? "  •  <color=#FF8E1A>ENRAGED ×" + boss.ragePhaseTriggerCount + "</color>"
                : "";
            // Sudden Death: appended in red once the fight has dragged on past
            // the suddenDeathAt threshold (default 8 minutes). Cosmetic.
            string suddenStr = boss.IsSuddenDeath
                ? "  •  <color=#FF2D2D>SUDDEN DEATH</color>"
                : "";
            bossBarLabel.text = dr > 0
                ? "SLIME GOD  •  " + dr + "% DMG REDUCTION" + shieldStr + rageStr + suddenStr
                : "SLIME GOD" + shieldStr + rageStr + suddenStr;
        }
    }

    private void BuildBossBar(Transform parent)
    {
        // Bottom-center container, centered horizontally with a fixed width.
        GameObject root = MakeUIObject("BossBar", parent);
        bossBarRoot = root;
        RectTransform rt = (RectTransform)root.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bossBarBottomMargin);
        rt.sizeDelta = new Vector2(bossBarWidth, bossBarHeight + bossBarFontSize + 6);

        // Label (boss name + damage reduction %, sits ABOVE the bar).
        GameObject lblGo = MakeUIObject("BossLabel", root.transform);
        RectTransform lblRt = (RectTransform)lblGo.transform;
        lblRt.anchorMin = new Vector2(0f, 1f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.pivot = new Vector2(0.5f, 1f);
        lblRt.anchoredPosition = new Vector2(0f, 0f);
        lblRt.sizeDelta = new Vector2(0f, bossBarFontSize + 4f);

        bossBarLabel = lblGo.AddComponent<Text>();
        bossBarLabel.font = defaultFont;
        bossBarLabel.fontSize = bossBarFontSize;
        bossBarLabel.color = new Color(1f, 0.92f, 0.6f);
        bossBarLabel.alignment = TextAnchor.LowerCenter;
        bossBarLabel.fontStyle = FontStyle.Bold;
        bossBarLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        bossBarLabel.verticalOverflow = VerticalWrapMode.Overflow;
        bossBarLabel.raycastTarget = false;
        bossBarLabel.text = "SLIME GOD";
        AddTextOutline(lblGo);

        // Bar background, anchored to the bottom of the container.
        GameObject bgGo = MakeUIObject("BossBarBG", root.transform);
        RectTransform bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = new Vector2(0f, 0f);
        bgRt.anchorMax = new Vector2(1f, 0f);
        bgRt.pivot = new Vector2(0.5f, 0f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(0f, bossBarHeight);

        Image bgImg = bgGo.AddComponent<Image>();
        bgImg.color = bossBarBgColor;
        bgImg.raycastTarget = false;
        AddOutline(bgGo);

        // Fill — left-anchored, growing rightward via sizeDelta (matches the
        // pattern used for the player HP bar, which avoids Image.Type.Filled
        // needing a sprite).
        GameObject fillGo = MakeUIObject("BossBarFill", bgGo.transform);
        bossBarFillRect = (RectTransform)fillGo.transform;
        bossBarFillRect.anchorMin = new Vector2(0f, 0f);
        bossBarFillRect.anchorMax = new Vector2(0f, 1f);
        bossBarFillRect.pivot = new Vector2(0f, 0.5f);
        bossBarFillRect.anchoredPosition = new Vector2(0f, 0f);
        bossBarFillRect.sizeDelta = new Vector2(bossBarWidth, 0f);

        bossBarFillImg = fillGo.AddComponent<Image>();
        bossBarFillImg.color = bossBarColor;
        bossBarFillImg.raycastTarget = false;

        bossBarMaxWidth = bossBarWidth;

        // HP text on top of the bar.
        GameObject txtGo = MakeUIObject("BossBarText", bgGo.transform);
        RectTransform txtRt = (RectTransform)txtGo.transform;
        txtRt.anchorMin = new Vector2(0f, 0f);
        txtRt.anchorMax = new Vector2(1f, 1f);
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        bossBarText = txtGo.AddComponent<Text>();
        bossBarText.font = defaultFont;
        bossBarText.fontSize = Mathf.Max(14, bossBarHeight - 8);
        bossBarText.color = Color.white;
        bossBarText.alignment = TextAnchor.MiddleCenter;
        bossBarText.fontStyle = FontStyle.Bold;
        bossBarText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bossBarText.verticalOverflow = VerticalWrapMode.Overflow;
        bossBarText.raycastTarget = false;
        bossBarText.text = "0 / 0";
        AddTextOutline(txtGo);

        bossBarRoot.SetActive(false);
    }

    // -------------------- UI construction --------------------

    private void BuildHUD()
    {
        // Root canvas
        GameObject canvasGo = new GameObject("HUDCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        BuildTimer(canvasGo.transform);
        BuildScore(canvasGo.transform);
        BuildHealth(canvasGo.transform);
        BuildWeaponSlots(canvasGo.transform);
        BuildBossBar(canvasGo.transform);
        BuildXPBar(canvasGo.transform);
    }

    /// <summary>
    /// Slim XP bar pinned just under the timer (and the boss sub-timer when
    /// it's visible). Reads GameManager.LevelProgress for fill and shows the
    /// current character level on the right end of the bar.
    /// </summary>
    private void BuildXPBar(Transform parent)
    {
        // Sit a small gap below the boss-timer's vertical slot so the layout
        // stays clean whether or not the boss timer is visible (the boss
        // timer text is just empty when hidden — same vertical real estate).
        int bossFontSize = Mathf.Max(18, timerFontSize / 2);
        float yOffset = margin + timerFontSize + 6 + bossFontSize + 14;

        const float barW = 320f;
        const float barH = 14f;

        GameObject bgGo = MakeUIObject("XPBarBG", parent);
        RectTransform bgRt = (RectTransform)bgGo.transform;
        AnchorTopLeft(bgRt, margin, yOffset, barW, barH);
        Image bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;

        // Fill rect: anchored bottom-left of the bg, top edge at full
        // height. The RIGHT edge is set every frame in UpdateXPBar via
        // anchorMax.x = progress (0..1). Using anchor-based scaling
        // instead of Image.Type.Filled so we don't depend on a sprite.
        GameObject fillGo = MakeUIObject("XPBarFill", bgGo.transform);
        RectTransform fillRt = (RectTransform)fillGo.transform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f); // anchorMax.x updated per-frame
        fillRt.pivot     = new Vector2(0f, 0.5f);
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(0f, -2f);
        xpBarFill = fillGo.AddComponent<Image>();
        xpBarFill.color = new Color(1f, 0.85f, 0.3f, 1f);  // gold (matches the LEVEL UP! header)
        xpBarFill.raycastTarget = false;

        GameObject lblGo = MakeUIObject("XPBarLabel", bgGo.transform);
        RectTransform lblRt = (RectTransform)lblGo.transform;
        lblRt.anchorMin = new Vector2(0f, 0f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.offsetMin = new Vector2(8f, 0f);
        lblRt.offsetMax = new Vector2(-8f, 0f);
        xpBarLabel = lblGo.AddComponent<Text>();
        xpBarLabel.font = defaultFont;
        xpBarLabel.fontSize = 12;
        xpBarLabel.color = textColor;
        xpBarLabel.alignment = TextAnchor.MiddleRight;
        xpBarLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        xpBarLabel.raycastTarget = false;
        xpBarLabel.text = "Lv 1   0 / 50";
        AddTextOutline(lblGo);
    }

    private void UpdateXPBar()
    {
        if (xpBarFill == null) return;
        if (gameManager == null) gameManager = GameManager.Instance;
        if (gameManager == null) return;
        // Drive the fill via anchor scaling — anchorMax.x = progress (0..1).
        // This grows the gold rect from left to right as XP accumulates.
        var rt = (RectTransform)xpBarFill.transform;
        Vector2 max = rt.anchorMax;
        max.x = Mathf.Clamp01(gameManager.LevelProgress);
        rt.anchorMax = max;
        if (xpBarLabel != null)
        {
            if (gameManager.IsMaxLevel)
                xpBarLabel.text = "Lv " + gameManager.CurrentLevel + "  MAX";
            else
                xpBarLabel.text = "Lv " + gameManager.CurrentLevel
                                + "   " + gameManager.CurrentXP
                                + " / " + gameManager.NextLevelXP;
        }
    }

    private void BuildTimer(Transform parent)
    {
        // Top-left "00:00" — just text, no panel, keeps the screen clean.
        GameObject go = MakeUIObject("Timer", parent);
        RectTransform rt = (RectTransform)go.transform;
        AnchorTopLeft(rt, margin, margin, 360, 80);

        timerText = go.AddComponent<Text>();
        timerText.font = defaultFont;
        timerText.fontSize = timerFontSize;
        timerText.color = textColor;
        timerText.alignment = TextAnchor.UpperLeft;
        timerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        timerText.verticalOverflow = VerticalWrapMode.Overflow;
        timerText.raycastTarget = false;
        timerText.text = "00:00";

        AddTextOutline(go);

        // Boss sub-timer: smaller, sits right under the main timer. Hidden
        // until a SlimeGod is alive (UpdateBossTimer toggles visibility).
        GameObject bossGo = MakeUIObject("BossTimer", parent);
        RectTransform bossRt = (RectTransform)bossGo.transform;
        int bossFontSize = Mathf.Max(18, timerFontSize / 2);
        AnchorTopLeft(bossRt, margin, margin + timerFontSize + 6, 360, bossFontSize + 8);

        bossTimerText = bossGo.AddComponent<Text>();
        bossTimerText.font = defaultFont;
        bossTimerText.fontSize = bossFontSize;
        bossTimerText.color = new Color(1f, 0.55f, 0.45f, 1f);
        bossTimerText.alignment = TextAnchor.UpperLeft;
        bossTimerText.fontStyle = FontStyle.Bold;
        bossTimerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bossTimerText.verticalOverflow = VerticalWrapMode.Overflow;
        bossTimerText.raycastTarget = false;
        bossTimerText.text = "Boss 00:00";
        AddTextOutline(bossGo);
        bossGo.SetActive(false);
    }

    private void BuildScore(Transform parent)
    {
        // -------- Current score (large, top of stack) --------
        GameObject go = MakeUIObject("Score", parent);
        RectTransform rt = (RectTransform)go.transform;
        AnchorTopRight(rt, margin, margin, 360, 60);

        scoreText = go.AddComponent<Text>();
        scoreText.font = defaultFont;
        scoreText.fontSize = scoreFontSize;
        scoreText.color = textColor;
        scoreText.alignment = TextAnchor.UpperRight;
        scoreText.horizontalOverflow = HorizontalWrapMode.Overflow;
        scoreText.verticalOverflow = VerticalWrapMode.Overflow;
        scoreText.raycastTarget = false;
        scoreText.text = "Score  0";

        AddTextOutline(go);

        // -------- High score (smaller, sits under the score) --------
        GameObject hsGo = MakeUIObject("HighScore", parent);
        RectTransform hsRt = (RectTransform)hsGo.transform;
        AnchorTopRight(hsRt, margin, margin + scoreFontSize + 6, 360, 36);

        highScoreText = hsGo.AddComponent<Text>();
        highScoreText.font = defaultFont;
        highScoreText.fontSize = Mathf.Max(16, scoreFontSize - 14);
        highScoreText.color = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        highScoreText.alignment = TextAnchor.UpperRight;
        highScoreText.horizontalOverflow = HorizontalWrapMode.Overflow;
        highScoreText.verticalOverflow = VerticalWrapMode.Overflow;
        highScoreText.raycastTarget = false;
        highScoreText.text = "Best  0";
        AddTextOutline(hsGo);

        // -------- "NEW HIGH SCORE!" banner (gold, pulses, hidden until earned) --------
        GameObject nhGo = MakeUIObject("NewHighScore", parent);
        RectTransform nhRt = (RectTransform)nhGo.transform;
        AnchorTopRight(nhRt, margin, margin + scoreFontSize + 6 + 38, 420, 38);

        newHighScoreText = nhGo.AddComponent<Text>();
        newHighScoreText.font = defaultFont;
        newHighScoreText.fontSize = Mathf.Max(18, scoreFontSize - 12);
        newHighScoreText.color = new Color(1f, 0.85f, 0.2f);
        newHighScoreText.alignment = TextAnchor.UpperRight;
        newHighScoreText.fontStyle = FontStyle.Bold;
        newHighScoreText.horizontalOverflow = HorizontalWrapMode.Overflow;
        newHighScoreText.verticalOverflow = VerticalWrapMode.Overflow;
        newHighScoreText.raycastTarget = false;
        newHighScoreText.text = "★ NEW HIGH SCORE!";
        AddTextOutline(nhGo);
        nhGo.SetActive(false); // hidden until the player surpasses their previous best
    }

    private void BuildHealth(Transform parent)
    {
        // Container at bottom-left holding [bar background] + [bar fill] + [HP text on top].
        GameObject container = MakeUIObject("Health", parent);
        RectTransform crt = (RectTransform)container.transform;
        AnchorBottomLeft(crt, margin, margin, healthBarWidth, healthBarHeight + hpFontSize + 8);

        // HP text sits above the bar
        GameObject txtGo = MakeUIObject("HPText", container.transform);
        RectTransform trt = (RectTransform)txtGo.transform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(2f, 0f);
        trt.sizeDelta = new Vector2(0f, hpFontSize + 4f);

        healthText = txtGo.AddComponent<Text>();
        healthText.font = defaultFont;
        healthText.fontSize = hpFontSize;
        healthText.color = textColor;
        healthText.alignment = TextAnchor.LowerLeft;
        healthText.raycastTarget = false;
        healthText.text = "100 / 100";
        AddTextOutline(txtGo);

        // Background of the health bar (anchored to the bottom of the container).
        GameObject bgGo = MakeUIObject("Bar BG", container.transform);
        RectTransform bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = new Vector2(0f, 0f);
        bgRt.anchorMax = new Vector2(1f, 0f);
        bgRt.pivot = new Vector2(0f, 0f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(0f, healthBarHeight);

        Image bgImg = bgGo.AddComponent<Image>();
        bgImg.color = healthBackgroundColor;
        bgImg.raycastTarget = false;

        AddOutline(bgGo);

        // Foreground (the colored fill).
        GameObject fillGo = MakeUIObject("Bar Fill", bgGo.transform);
        healthFillRect = (RectTransform)fillGo.transform;
        healthFillRect.anchorMin = new Vector2(0f, 0f);
        healthFillRect.anchorMax = new Vector2(0f, 1f);
        healthFillRect.pivot = new Vector2(0f, 0.5f);
        healthFillRect.anchoredPosition = new Vector2(0f, 0f);
        healthFillRect.sizeDelta = new Vector2(healthBarWidth, 0f);

        healthFillImg = fillGo.AddComponent<Image>();
        healthFillImg.color = healthColor;
        healthFillImg.raycastTarget = false;

        healthBarMaxWidth = healthBarWidth;
    }

    private void BuildWeaponSlots(Transform parent)
    {
        // Container at bottom-right. Three square slots laid out horizontally:
        //   [DASH]  [PRIMARY (LMB)]  [SECONDARY (RMB)]
        GameObject container = MakeUIObject("WeaponSlots", parent);
        RectTransform crt = (RectTransform)container.transform;
        int totalWidth = weaponSlotSize * 3 + weaponSlotSpacing * 2;
        int totalHeight = weaponSlotSize + weaponNameFontSize + 8;
        AnchorBottomRight(crt, margin, margin, totalWidth, totalHeight);

        int x = 0;
        dashSlot      = BuildDashSlot(container.transform, x);
        x += weaponSlotSize + weaponSlotSpacing;
        primarySlot   = BuildSlot(container.transform, x, "PrimarySlot");
        x += weaponSlotSize + weaponSlotSpacing;
        secondarySlot = BuildSlot(container.transform, x, "SecondarySlot");

        // Tank ability slot — top-level canvas child so it sits ABOVE the
        // weapon-slots container without changing its layout. Aligned in X
        // with the dash slot and offset upward by one slot height + gap.
        tankSlot = BuildTankSlot(parent, totalWidth, totalHeight);
        tankSlot.root.SetActive(false); // hidden until iAmTankActive

        // Meteor Rain slot — same screen position as the Tank slot (they're
        // mutex via slot-3 lockout). Both slots exist; only one is ever
        // visible because the player picks one general augment per run.
        meteorSlot = BuildTankSlot(parent, totalWidth, totalHeight);
        meteorSlot.root.name = "MeteorRainAbilitySlot";
        if (meteorAbilityIcon != null) meteorSlot.iconImage.sprite = meteorAbilityIcon;
        else                            meteorSlot.iconImage.color = new Color(1f, 0.55f, 0.2f, 0.22f); // faint orange placeholder
        meteorSlot.root.SetActive(false); // hidden until meteorEnabled
    }

    private TankWidgets BuildTankSlot(Transform parent, int weaponContainerWidth, int weaponContainerHeight)
    {
        TankWidgets ts = new TankWidgets();

        // Square slot positioned to the LEFT of the dash slot at the same
        // vertical Y. The dash slot sits at the WeaponSlots container's
        // top-left, with the container itself anchored bottom-right at
        // (margin, margin) with size (totalWidth, totalHeight = weaponSlot
        // + caption). Tank's right edge is one slotWidth+spacing to the
        // left of the container; bottom Y matches the dash slot's bottom.
        int captionRow = weaponNameFontSize + 8; // height of the "MMB" / "Space" caption row beneath each slot
        GameObject slotGo = MakeUIObject("TankAbilitySlot", parent);
        RectTransform rt = (RectTransform)slotGo.transform;
        AnchorBottomRight(
            rt,
            margin + weaponContainerWidth + weaponSlotSpacing,
            margin + captionRow,
            weaponSlotSize,
            weaponSlotSize);

        Image bgImg = slotGo.AddComponent<Image>();
        bgImg.color = panelColor;
        bgImg.raycastTarget = false;
        AddOutline(slotGo);
        ts.root = slotGo;

        // Optional sprite icon (sits behind the cooldown overlay so it dims
        // while the ability is recharging).
        GameObject iconGo = MakeUIObject("Icon", slotGo.transform);
        RectTransform iconRt = (RectTransform)iconGo.transform;
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(8f, 8f);
        iconRt.offsetMax = new Vector2(-8f, -8f);
        ts.iconImage = iconGo.AddComponent<Image>();
        ts.iconImage.preserveAspect = true;
        ts.iconImage.raycastTarget = false;
        ts.iconImage.color = new Color(1f, 1f, 1f, 0.85f);
        if (tankAbilityIcon != null) ts.iconImage.sprite = tankAbilityIcon;
        else                         ts.iconImage.color = new Color(0.4f, 0.7f, 1f, 0.18f); // faint cyan placeholder

        // Cooldown overlay drains top-down (matches dash + weapon slots).
        GameObject cdGo = MakeUIObject("Cooldown", slotGo.transform);
        RectTransform cdRt = (RectTransform)cdGo.transform;
        cdRt.anchorMin = Vector2.zero;
        cdRt.anchorMax = Vector2.one;
        cdRt.offsetMin = Vector2.zero;
        cdRt.offsetMax = Vector2.zero;

        ts.cooldownOverlay = cdGo.AddComponent<Image>();
        ts.cooldownOverlay.color = cooldownOverlayColor;
        ts.cooldownOverlay.type = Image.Type.Filled;
        ts.cooldownOverlay.fillMethod = Image.FillMethod.Vertical;
        ts.cooldownOverlay.fillOrigin = (int)Image.OriginVertical.Top;
        ts.cooldownOverlay.fillAmount = 0f;
        ts.cooldownOverlay.raycastTarget = false;

        // Label — "TANK" when ready, seconds-remaining while cooling down.
        GameObject labelGo = MakeUIObject("Label", slotGo.transform);
        RectTransform lblRt = (RectTransform)labelGo.transform;
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;

        ts.labelText = labelGo.AddComponent<Text>();
        ts.labelText.font = defaultFont;
        ts.labelText.fontSize = 22;
        ts.labelText.color = textColor;
        ts.labelText.alignment = TextAnchor.MiddleCenter;
        ts.labelText.fontStyle = FontStyle.Bold;
        ts.labelText.text = "TANK";
        ts.labelText.raycastTarget = false;
        AddTextOutline(labelGo);

        // Caption underneath: "MMB" so the player learns the keybind.
        GameObject captionGo = MakeUIObject("Caption", slotGo.transform);
        RectTransform capRt = (RectTransform)captionGo.transform;
        capRt.anchorMin = new Vector2(0f, 0f);
        capRt.anchorMax = new Vector2(1f, 0f);
        capRt.pivot = new Vector2(0.5f, 1f);
        capRt.anchoredPosition = new Vector2(0f, -4f);
        capRt.sizeDelta = new Vector2(0f, weaponNameFontSize + 4f);

        Text caption = captionGo.AddComponent<Text>();
        caption.font = defaultFont;
        caption.fontSize = weaponNameFontSize;
        caption.color = textColor;
        caption.alignment = TextAnchor.UpperCenter;
        caption.text = "MMB";
        caption.raycastTarget = false;
        AddTextOutline(captionGo);

        return ts;
    }

    private void UpdateTankSlot()
    {
        if (tankSlot.root == null || hero == null) return;

        // Show only after the player has accepted the I-am-Tank upgrade.
        bool active = hero.iAmTankActive;
        if (tankSlot.root.activeSelf != active) tankSlot.root.SetActive(active);
        if (!active) return;

        float total = Mathf.Max(0.001f, hero.iAmTankAbilityCooldown);
        float remaining = total * (1f - hero.TankAbilityCooldownProgress);

        if (hero.IsTankShieldActive)
        {
            // Shield up — show shield duration countdown in green-ish so it
            // reads as a buff state, not a disabled state.
            float shieldLeft = hero.TankShieldTimeRemaining;
            tankSlot.labelText.text = shieldLeft >= 1f ? shieldLeft.ToString("0") : shieldLeft.ToString("0.0");
            tankSlot.labelText.color = new Color(0.6f, 1f, 0.6f, 1f);
            // No cooldown overlay while shield is up — recasting refreshes anyway.
            if (tankSlot.cooldownOverlay != null) tankSlot.cooldownOverlay.fillAmount = 0f;
        }
        else if (remaining > 0.05f)
        {
            // On cooldown.
            tankSlot.labelText.text = remaining >= 1f ? remaining.ToString("0") : remaining.ToString("0.0");
            tankSlot.labelText.color = textColor;
            if (tankSlot.cooldownOverlay != null)
                tankSlot.cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / total);
        }
        else
        {
            // Ready.
            tankSlot.labelText.text = "TANK";
            tankSlot.labelText.color = textColor;
            if (tankSlot.cooldownOverlay != null) tankSlot.cooldownOverlay.fillAmount = 0f;
        }
    }

    private void UpdateMeteorSlot()
    {
        if (meteorSlot.root == null || hero == null) return;

        // Show only when the meteor augment owns MMB. IsMeteorRainAvailable
        // collapses (meteorEnabled && !iAmTankActive), which mirrors the
        // slot-3 mutex (only one general augment per run).
        bool active = hero.IsMeteorRainAvailable;
        if (meteorSlot.root.activeSelf != active) meteorSlot.root.SetActive(active);
        if (!active) return;

        float total = Mathf.Max(0.001f, hero.meteorRainCooldown);
        float remaining = hero.MeteorRainCooldownRemaining;

        if (hero.IsMeteorRainActive)
        {
            // Mid-cast — flash a label that reads as "rain falling now"
            // so the player can tell their cast went off even though the
            // cooldown immediately starts ticking.
            meteorSlot.labelText.text = "RAIN!";
            meteorSlot.labelText.color = new Color(1f, 0.7f, 0.3f, 1f);
            if (meteorSlot.cooldownOverlay != null)
                meteorSlot.cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / total);
        }
        else if (remaining > 0.05f)
        {
            // On cooldown.
            meteorSlot.labelText.text = remaining >= 1f ? remaining.ToString("0") : remaining.ToString("0.0");
            meteorSlot.labelText.color = textColor;
            if (meteorSlot.cooldownOverlay != null)
                meteorSlot.cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / total);
        }
        else
        {
            // Ready.
            meteorSlot.labelText.text = "RAIN";
            meteorSlot.labelText.color = textColor;
            if (meteorSlot.cooldownOverlay != null) meteorSlot.cooldownOverlay.fillAmount = 0f;
        }
    }

    private DashWidgets BuildDashSlot(Transform parent, int xOffset)
    {
        DashWidgets ds = new DashWidgets();

        // Square slot background
        GameObject slotGo = MakeUIObject("DashSlot", parent);
        RectTransform rt = (RectTransform)slotGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(xOffset, 0f);
        rt.sizeDelta = new Vector2(weaponSlotSize, weaponSlotSize);

        Image bgImg = slotGo.AddComponent<Image>();
        bgImg.color = panelColor;
        bgImg.raycastTarget = false;
        AddOutline(slotGo);
        ds.root = slotGo;

        // Cooldown overlay drains top-down (matches the weapon slots).
        GameObject cdGo = MakeUIObject("Cooldown", slotGo.transform);
        RectTransform cdRt = (RectTransform)cdGo.transform;
        cdRt.anchorMin = Vector2.zero;
        cdRt.anchorMax = Vector2.one;
        cdRt.offsetMin = Vector2.zero;
        cdRt.offsetMax = Vector2.zero;

        ds.cooldownOverlay = cdGo.AddComponent<Image>();
        ds.cooldownOverlay.color = cooldownOverlayColor;
        ds.cooldownOverlay.type = Image.Type.Filled;
        ds.cooldownOverlay.fillMethod = Image.FillMethod.Vertical;
        ds.cooldownOverlay.fillOrigin = (int)Image.OriginVertical.Top;
        ds.cooldownOverlay.fillAmount = 0f;
        ds.cooldownOverlay.raycastTarget = false;

        // Big centered label — shows "DASH" when ready, "0.5" (seconds) when
        // on cooldown. Renders above the overlay so the number stays readable
        // through the dim mask.
        GameObject labelGo = MakeUIObject("Label", slotGo.transform);
        RectTransform lblRt = (RectTransform)labelGo.transform;
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;

        ds.labelText = labelGo.AddComponent<Text>();
        ds.labelText.font = defaultFont;
        ds.labelText.fontSize = 28;
        ds.labelText.color = textColor;
        ds.labelText.alignment = TextAnchor.MiddleCenter;
        ds.labelText.fontStyle = FontStyle.Bold;
        ds.labelText.text = "DASH";
        ds.labelText.raycastTarget = false;
        AddTextOutline(labelGo);

        // Caption underneath: "Space" so the player learns the keybind.
        GameObject captionGo = MakeUIObject("Caption", slotGo.transform);
        RectTransform capRt = (RectTransform)captionGo.transform;
        capRt.anchorMin = new Vector2(0f, 0f);
        capRt.anchorMax = new Vector2(1f, 0f);
        capRt.pivot = new Vector2(0.5f, 1f);
        capRt.anchoredPosition = new Vector2(0f, -4f);
        capRt.sizeDelta = new Vector2(0f, weaponNameFontSize + 4f);

        Text caption = captionGo.AddComponent<Text>();
        caption.font = defaultFont;
        caption.fontSize = weaponNameFontSize;
        caption.color = textColor;
        caption.alignment = TextAnchor.UpperCenter;
        caption.text = "Space";
        caption.raycastTarget = false;
        AddTextOutline(captionGo);

        return ds;
    }

    private Slot BuildSlot(Transform parent, int xOffset, string slotName)
    {
        Slot slot = new Slot();

        // Square slot background
        GameObject slotGo = MakeUIObject(slotName, parent);
        slot.root = slotGo;
        RectTransform rt = (RectTransform)slotGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(xOffset, 0f);
        rt.sizeDelta = new Vector2(weaponSlotSize, weaponSlotSize);

        Image bgImg = slotGo.AddComponent<Image>();
        bgImg.color = panelColor;
        bgImg.raycastTarget = false;
        AddOutline(slotGo);

        // Icon image (fills the slot, leaves a small inset).
        GameObject iconGo = MakeUIObject("Icon", slotGo.transform);
        RectTransform iconRt = (RectTransform)iconGo.transform;
        iconRt.anchorMin = new Vector2(0f, 0f);
        iconRt.anchorMax = new Vector2(1f, 1f);
        iconRt.offsetMin = new Vector2(8f, 8f);
        iconRt.offsetMax = new Vector2(-8f, -8f);

        slot.icon = iconGo.AddComponent<Image>();
        slot.icon.preserveAspect = true;
        slot.icon.raycastTarget = false;
        slot.icon.color = new Color(1f, 1f, 1f, 0.08f);

        // Cooldown overlay (a black fill that drains from top to bottom while
        // the weapon is on cooldown). Image set to Filled / Vertical / Top.
        GameObject cdGo = MakeUIObject("Cooldown", slotGo.transform);
        RectTransform cdRt = (RectTransform)cdGo.transform;
        cdRt.anchorMin = new Vector2(0f, 0f);
        cdRt.anchorMax = new Vector2(1f, 1f);
        cdRt.offsetMin = Vector2.zero;
        cdRt.offsetMax = Vector2.zero;

        slot.cooldownOverlay = cdGo.AddComponent<Image>();
        slot.cooldownOverlay.color = cooldownOverlayColor;
        slot.cooldownOverlay.type = Image.Type.Filled;
        slot.cooldownOverlay.fillMethod = Image.FillMethod.Vertical;
        slot.cooldownOverlay.fillOrigin = (int)Image.OriginVertical.Top;
        slot.cooldownOverlay.fillAmount = 0f;
        slot.cooldownOverlay.raycastTarget = false;

        // Name label sitting under the slot
        GameObject nameGo = MakeUIObject("Name", slotGo.transform);
        RectTransform nameRt = (RectTransform)nameGo.transform;
        nameRt.anchorMin = new Vector2(0f, 0f);
        nameRt.anchorMax = new Vector2(1f, 0f);
        nameRt.pivot = new Vector2(0.5f, 1f);
        nameRt.anchoredPosition = new Vector2(0f, -4f);
        nameRt.sizeDelta = new Vector2(0f, weaponNameFontSize + 4f);

        slot.nameText = nameGo.AddComponent<Text>();
        slot.nameText.font = defaultFont;
        slot.nameText.fontSize = weaponNameFontSize;
        slot.nameText.color = textColor;
        slot.nameText.alignment = TextAnchor.UpperCenter;
        slot.nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        slot.nameText.verticalOverflow = VerticalWrapMode.Overflow;
        slot.nameText.raycastTarget = false;
        slot.nameText.text = "-";
        AddTextOutline(nameGo);

        return slot;
    }

    // -------------------- Helpers --------------------

    private GameObject MakeUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private void AnchorTopLeft(RectTransform rt, float xOffset, float yOffset, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(xOffset, -yOffset);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void AnchorTopRight(RectTransform rt, float xOffset, float yOffset, float w, float h)
    {
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-xOffset, -yOffset);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void AnchorBottomLeft(RectTransform rt, float xOffset, float yOffset, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(xOffset, yOffset);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void AnchorBottomRight(RectTransform rt, float xOffset, float yOffset, float w, float h)
    {
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-xOffset, yOffset);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void AddTextOutline(GameObject go)
    {
        UnityEngine.UI.Outline o = go.AddComponent<UnityEngine.UI.Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.85f);
        o.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private void AddOutline(GameObject go)
    {
        UnityEngine.UI.Outline o = go.AddComponent<UnityEngine.UI.Outline>();
        o.effectColor = outlineColor;
        o.effectDistance = new Vector2(1f, -1f);
    }
}
