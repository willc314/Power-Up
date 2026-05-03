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
    public int margin = 24;
    public int timerFontSize = 56;
    public int scoreFontSize = 36;
    public int hpFontSize = 22;
    public int weaponNameFontSize = 18;
    public int weaponSlotSize = 96;
    public int weaponSlotSpacing = 12;
    public int healthBarWidth = 320;
    public int healthBarHeight = 22;

    [Header("Mouse-button labels")]
    public string primaryButtonLabel = "LMB";
    public string secondaryButtonLabel = "RMB";

    // ---- Built UI references ----
    private Canvas canvas;
    private Text timerText;
    private Text scoreText;
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
    }

    // -------------------- Per-frame updates --------------------

    private void UpdateTimer()
    {
        if (timerText == null) return;
        float t = gameManager != null ? gameManager.ElapsedTime : 0f;
        timerText.text = GameManager.FormatTime(t);
    }

    private void UpdateScore()
    {
        if (scoreText == null) return;
        int s = gameManager != null ? gameManager.Score : 0;
        scoreText.text = "Score  " + s.ToString();
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

        slot.nameText.text = buttonLabel + "  -  " + (string.IsNullOrEmpty(w.weaponName) ? w.GetType().Name : w.weaponName);

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
    }

    private void BuildScore(Transform parent)
    {
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
        // Container at bottom-right with two square slots laid out horizontally.
        GameObject container = MakeUIObject("WeaponSlots", parent);
        RectTransform crt = (RectTransform)container.transform;
        int totalWidth = weaponSlotSize * 2 + weaponSlotSpacing;
        int totalHeight = weaponSlotSize + weaponNameFontSize + 8;
        AnchorBottomRight(crt, margin, margin, totalWidth, totalHeight);

        // Build secondary on the right, primary on the left.
        primarySlot   = BuildSlot(container.transform, 0,                         "PrimarySlot");
        secondarySlot = BuildSlot(container.transform, weaponSlotSize + weaponSlotSpacing, "SecondarySlot");
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
