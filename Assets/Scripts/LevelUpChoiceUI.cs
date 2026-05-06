using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Modal picker shown when <see cref="GameManager.OnLeveledUp"/> fires.
/// Built entirely from script — the scene needs no UI prefab. Mirrors the
/// procedural style of <see cref="PowerUpChoiceUI"/> so the visual language
/// stays consistent.
///
/// Layout (3 panels max, empty slots hidden):
///   ┌────────────── dim background ──────────────┐
///   │              LEVEL UP! → 2                  │
///   │                                             │
///   │  ┌─────────┐  ┌─────────┐  ┌─────────┐     │
///   │  │  Title  │  │  Title  │  │  Title  │     │
///   │  │  [icon] │  │  [icon] │  │  [icon] │     │
///   │  │  desc…  │  │  desc…  │  │  desc…  │     │
///   │  │ SELECT  │  │ SELECT  │  │ SELECT  │     │
///   │  └─────────┘  └─────────┘  └─────────┘     │
///   │   Equipped     Other       General         │
///   └─────────────────────────────────────────────┘
///
/// Slot rules (matches LevelUpgrade.Slot):
///   * Slot 0 — random eligible upgrade for one of the EQUIPPED weapons.
///   * Slot 1 — random eligible upgrade for one of the NON-equipped weapons.
///   * Slot 2 — random eligible General Buff. Hidden permanently after the
///              player has accepted any general buff (one per run, per spec).
///
/// Pause / audio:
///   Time.timeScale → 0 while open, restored on Close. MusicManager ducked
///   via BeginDuck / EndDuck (ref-counted, paired). Multiple level-ups
///   landing in the same frame queue and resolve sequentially.
/// </summary>
public class LevelUpChoiceUI : MonoBehaviour
{
    public static LevelUpChoiceUI Instance { get; private set; }

    [Header("Style")]
    public Color overlayColor       = new Color(0f, 0f, 0f, 0.55f);
    public Color panelColor         = new Color(0.12f, 0.12f, 0.14f, 0.95f);
    public Color panelOutlineColor  = new Color(1f, 1f, 1f, 0.22f);
    public Color buttonColor        = new Color(0.22f, 0.22f, 0.26f, 1f);
    public Color buttonHoverColor   = new Color(0.32f, 0.34f, 0.40f, 1f);
    public Color buttonPressedColor = new Color(0.16f, 0.16f, 0.20f, 1f);
    public Color textColor          = Color.white;
    public Color subtleTextColor    = new Color(1f, 1f, 1f, 0.7f);
    public Color levelTextColor     = new Color(1f, 0.85f, 0.3f, 1f); // gold "LEVEL UP!"

    [Header("Layout")]
    public Vector2 panelSize        = new Vector2(280f, 480f);
    public float   panelSpacing     = 32f;
    public Vector2 iconSize         = new Vector2(160f, 160f);

    // ---- Runtime state ----
    private Canvas        canvas;
    private GameObject    rootContainer;     // dim overlay + panels
    private Text          headerText;
    private readonly Panel[] panels = new Panel[3];
    private float         savedTimeScale;
    private bool          shown;
    private bool          duckActive;
    private readonly Queue<int> pendingLevels = new Queue<int>(); // levels still to show after the current pick

    /// <summary>True while the picker is currently shown OR has queued levels still to drain.</summary>
    public bool IsActive => shown || pendingLevels.Count > 0;

    /// <summary>
    /// Fires when the picker fully closes (no queued levels left, time
    /// unpaused). PowerUpChoiceUI subscribes to drain its own deferred
    /// pickups once the level-up chain finishes.
    /// </summary>
    public static event System.Action OnClosed;

    private struct Panel
    {
        public GameObject root;
        public Text       title;
        public Image      icon;
        public Text       desc;
        public Button     select;
        public Text       slotLabel;
    }

    /// <summary>
    /// Auto-spawn the UI before the gameplay scene loads so other systems
    /// can rely on Instance existing. Same pattern as OptionsMenu.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("LevelUpChoiceUI");
        go.AddComponent<LevelUpChoiceUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Persist the auto-spawned singleton across scene transitions.
        // Without DontDestroyOnLoad the menu-scene-spawned UI gets destroyed
        // when the gameplay scene loads, AND RuntimeInitializeOnLoadMethod
        // only fires once per game launch — so the gameplay scene ends up
        // with no LevelUpChoiceUI listening, GameManager.OnLeveledUp fires
        // into the void (0 subscribers), and no UI ever pops. The editor
        // doesn't hit this because Play Mode usually starts in the
        // gameplay scene directly.
        DontDestroyOnLoad(gameObject);

        // Make sure the registry exists so the first level-up has somewhere
        // to roll from. Idempotent — calling Initialize re-creates the catalog.
        if (LevelUpgradeRegistry.Instance == null) LevelUpgradeRegistry.Initialize();

        BuildUI();
        Hide();
    }

    private void OnEnable()  { GameManager.OnLeveledUp += OnLeveledUp; }
    private void OnDisable() { GameManager.OnLeveledUp -= OnLeveledUp; }

    /// <summary>
    /// Triggered by GameManager when CurrentLevel advances. If the picker is
    /// already open (player just hit two thresholds in a row), the level is
    /// queued and shown after the current pick resolves. Also defers if a
    /// powerup choice UI is currently shown — the two pickers don't stack on
    /// screen; whichever is up first finishes before the other starts.
    /// </summary>
    private void OnLeveledUp(int newLevel)
    {
        Debug.Log($"[LevelUpChoiceUI] OnLeveledUp received: level={newLevel}");
        if (shown)
        {
            pendingLevels.Enqueue(newLevel);
            return;
        }
        if (PowerUpChoiceUI.Instance != null && PowerUpChoiceUI.Instance.IsActive)
        {
            // Wait for the powerup UI to close, then drain.
            pendingLevels.Enqueue(newLevel);
            PowerUpChoiceUI.OnClosed -= OnExternalUIClosed;
            PowerUpChoiceUI.OnClosed += OnExternalUIClosed;
            return;
        }
        Show(newLevel);
    }

    /// <summary>
    /// Subscribed to PowerUpChoiceUI.OnClosed when we deferred a level-up.
    /// Once the powerup UI fully closes, drain our pending queue.
    /// </summary>
    private void OnExternalUIClosed()
    {
        PowerUpChoiceUI.OnClosed -= OnExternalUIClosed;
        if (shown || pendingLevels.Count == 0) return;
        Show(pendingLevels.Dequeue());
    }

    private void Show(int level)
    {
        if (Hero.Instance == null || Hero.Instance.IsDead)
        {
            Debug.Log($"[LevelUpChoiceUI] Show({level}) early-out: Hero.Instance={(Hero.Instance != null ? "OK" : "null")}, IsDead={(Hero.Instance != null ? Hero.Instance.IsDead.ToString() : "n/a")}");
            return;
        }

        var registry = LevelUpgradeRegistry.Instance ?? LevelUpgradeRegistry.Initialize();
        LevelUpgrade[] offer = registry.RollOffer(Hero.Instance);

        // Hide entirely if NO slot has an upgrade — nothing to choose. This
        // is the early-game state when the upgrade catalog is empty (Phase 1
        // before phases 2-4 register their upgrades). Drain the queue first
        // so we don't deadlock on a future level that does have offers.
        bool anyOffer = false;
        for (int i = 0; i < offer.Length; i++) if (offer[i] != null) { anyOffer = true; break; }
        Debug.Log($"[LevelUpChoiceUI] Show({level}) registry rolled offer: equipped={(offer.Length > 0 && offer[0] != null ? offer[0].DisplayName : "null")}, other={(offer.Length > 1 && offer[1] != null ? offer[1].DisplayName : "null")}, general={(offer.Length > 2 && offer[2] != null ? offer[2].DisplayName : "null")}");
        if (!anyOffer)
        {
            // Process the next queued level (recursive, but bounded by the
            // queue size, so no risk of infinite recursion).
            if (pendingLevels.Count > 0) Show(pendingLevels.Dequeue());
            return;
        }

        // Cancel any in-progress weapon state BEFORE the panel pauses time.
        // Otherwise a held bow/crossbow visual stays in the world while the
        // game is paused (the OnFireUp event won't fire under timeScale = 0,
        // and even if the player releases during the pause, the visual is
        // already orphaned). Mirrors the same call in Hero.ApplyPowerUp so
        // both pickers — powerup and level-up — share interrupt semantics.
        // BowWeapon.OnInterrupted also tears down an active death beam so
        // it doesn't keep ticking through the paused screen.
        Hero hero = Hero.Instance;
        if (hero != null)
        {
            if (hero.primaryWeapon != null)   hero.primaryWeapon.OnInterrupted(hero);
            if (hero.secondaryWeapon != null) hero.secondaryWeapon.OnInterrupted(hero);
            hero.speedMultiplier = 1f;
        }

        rootContainer.SetActive(true);
        shown = true;
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        if (MusicManager.Instance != null) { MusicManager.Instance.BeginDuck(); duckActive = true; }

        // Player-facing language: "augment". Singular because each
        // level-up offers one pick (the level cap is 2, so there's only
        // ever one augment-choice event per run).
        headerText.text = "Choose your Augment";

        // Wire each panel to its offered upgrade (or hide if none).
        for (int i = 0; i < panels.Length; i++)
        {
            var slot = panels[i];
            var up = offer[i];
            if (up == null)
            {
                slot.root.SetActive(false);
                continue;
            }
            slot.root.SetActive(true);
            slot.title.text = up.DisplayName ?? "(unnamed)";
            slot.desc.text  = up.Description ?? "";
            slot.icon.sprite = up.Icon;
            slot.icon.color  = up.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.18f);
            slot.slotLabel.text = SlotLabel((LevelUpgrade.Slot)i);
            slot.select.onClick.RemoveAllListeners();
            // Capture by local so the closure binds to THIS upgrade, not a
            // loop-iteration variable that mutates underneath us.
            LevelUpgrade chosen = up;
            slot.select.onClick.AddListener(() => OnSelected(chosen));
        }

        EnsureEventSystem();
    }

    private void OnSelected(LevelUpgrade up)
    {
        if (up == null) return;
        try { up.Apply(Hero.Instance); }
        catch (System.Exception e) { Debug.LogException(e); }
        Close();
        // If another level-up landed while this one was open, kick off the next picker.
        if (pendingLevels.Count > 0) Show(pendingLevels.Dequeue());
    }

    private void Close()
    {
        if (!shown) return;
        shown = false;
        rootContainer.SetActive(false);
        Time.timeScale = savedTimeScale;
        // Pair the BeginDuck from Show. Ref-count guards against stray Hide() calls underflowing.
        if (duckActive)
        {
            duckActive = false;
            if (MusicManager.Instance != null) MusicManager.Instance.EndDuck();
        }
        // Fire only if we've FULLY closed (no queued levels left). This way
        // PowerUpChoiceUI doesn't try to drain its own queue mid-chain when
        // the player is about to see another level-up panel.
        if (pendingLevels.Count == 0)
        {
            try { OnClosed?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    private void Hide()
    {
        if (rootContainer != null) rootContainer.SetActive(false);
        shown = false;
    }

    private static string SlotLabel(LevelUpgrade.Slot slot)
    {
        switch (slot)
        {
            case LevelUpgrade.Slot.EquippedWeapon: return "EQUIPPED WEAPON AUGMENT";
            case LevelUpgrade.Slot.OtherWeapon:    return "OTHER WEAPON AUGMENT";
            case LevelUpgrade.Slot.GeneralBuff:    return "GENERAL AUGMENT";
        }
        return "";
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    // -------------------- UI construction --------------------

    private void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("LevelUpChoiceCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 220; // above HUD (100), HealthVignette (150), and PowerUpChoiceUI (200)
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Dim background that fills the screen and intercepts clicks.
        rootContainer = NewUIObject(canvasGo.transform, "Container");
        var rt = rootContainer.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var dim = rootContainer.AddComponent<Image>();
        dim.color = overlayColor;

        // Header text (top center).
        var headerGo = NewUIObject(rootContainer.transform, "Header");
        var headerRt = headerGo.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0.5f, 1f);
        headerRt.anchorMax = new Vector2(0.5f, 1f);
        headerRt.pivot     = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = new Vector2(0f, -60f);
        headerRt.sizeDelta = new Vector2(800f, 80f);
        headerText = headerGo.AddComponent<Text>();
        headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerText.alignment = TextAnchor.MiddleCenter;
        headerText.color = levelTextColor;
        headerText.fontSize = 56;
        headerText.fontStyle = FontStyle.Bold;
        headerText.text = "Choose your Augment";

        // Panels — 3 columns centered.
        float totalWidth = panels.Length * panelSize.x + (panels.Length - 1) * panelSpacing;
        float x0 = -totalWidth * 0.5f + panelSize.x * 0.5f;
        for (int i = 0; i < panels.Length; i++)
        {
            float cx = x0 + i * (panelSize.x + panelSpacing);
            panels[i] = BuildPanel(rootContainer.transform, new Vector2(cx, 0f));
        }
    }

    private Panel BuildPanel(Transform parent, Vector2 anchoredPos)
    {
        var p = new Panel();
        var go = NewUIObject(parent, "Panel");
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = panelSize;
        var bg = go.AddComponent<Image>();
        bg.color = panelColor;
        // Outline child
        var outlineGo = NewUIObject(go.transform, "Outline");
        var oRt = outlineGo.GetComponent<RectTransform>();
        oRt.anchorMin = Vector2.zero; oRt.anchorMax = Vector2.one;
        oRt.offsetMin = new Vector2(-2f, -2f);
        oRt.offsetMax = new Vector2(2f, 2f);
        var outline = outlineGo.AddComponent<Image>();
        outline.color = panelOutlineColor;
        outline.raycastTarget = false;
        outlineGo.transform.SetSiblingIndex(0); // behind the panel body

        p.root = go;

        // Layout strategy: each child is top-anchored or bottom-anchored
        // with absolute pixel offsets so positions are independent of the
        // panel's percentage height. This avoids the previous bug where
        // the description's anchorMax.y (0.55) put its top edge ABOVE the
        // icon's bottom edge whenever the panel height grew.
        const float TITLE_TOP    = 16f;
        const float TITLE_HEIGHT = 36f;
        const float ICON_TOP     = TITLE_TOP + TITLE_HEIGHT + 12f; // 64
        // Icon bottom = ICON_TOP + iconSize.y (160) = 224 from top
        const float DESC_TOP_GAP    = 16f;  // gap between icon bottom and description top
        const float DESC_HEIGHT     = 160f;
        const float SELECT_BOTTOM   = 30f;
        const float SELECT_HEIGHT   = 44f;
        const float SLOT_LABEL_BOTTOM = 6f;
        const float SLOT_LABEL_HEIGHT = 18f;

        // Title (top)
        var titleGo = NewUIObject(go.transform, "Title");
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot     = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -TITLE_TOP);
        titleRt.sizeDelta = new Vector2(-24f, TITLE_HEIGHT);
        p.title = titleGo.AddComponent<Text>();
        p.title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        p.title.alignment = TextAnchor.MiddleCenter;
        p.title.color = textColor;
        p.title.fontSize = 24;
        p.title.fontStyle = FontStyle.Bold;
        p.title.text = "Title";

        // Icon (centered, just below title)
        var iconGo = NewUIObject(go.transform, "Icon");
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 1f);
        iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot     = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = new Vector2(0f, -ICON_TOP);
        iconRt.sizeDelta = iconSize;
        p.icon = iconGo.AddComponent<Image>();
        p.icon.preserveAspect = true;
        p.icon.color = new Color(1f, 1f, 1f, 0.18f); // placeholder dim

        // Description — top-anchored at a known offset BELOW the icon, with
        // a fixed height. Absolute positioning means even if panelSize.y
        // shrinks/grows, the description never overlaps the icon.
        float descTopOffset = ICON_TOP + iconSize.y + DESC_TOP_GAP;
        var descGo = NewUIObject(go.transform, "Desc");
        var descRt = descGo.GetComponent<RectTransform>();
        descRt.anchorMin = new Vector2(0f, 1f);
        descRt.anchorMax = new Vector2(1f, 1f);
        descRt.pivot     = new Vector2(0.5f, 1f);
        descRt.anchoredPosition = new Vector2(0f, -descTopOffset);
        descRt.sizeDelta = new Vector2(-32f, DESC_HEIGHT);
        p.desc = descGo.AddComponent<Text>();
        p.desc.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        p.desc.alignment = TextAnchor.UpperCenter;
        p.desc.color = subtleTextColor;
        p.desc.fontSize = 16;
        p.desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        p.desc.verticalOverflow = VerticalWrapMode.Truncate;
        p.desc.text = "";

        // Slot label (very bottom, small)
        var slotLabelGo = NewUIObject(go.transform, "SlotLabel");
        var slRt = slotLabelGo.GetComponent<RectTransform>();
        slRt.anchorMin = new Vector2(0f, 0f);
        slRt.anchorMax = new Vector2(1f, 0f);
        slRt.pivot     = new Vector2(0.5f, 0f);
        slRt.anchoredPosition = new Vector2(0f, SLOT_LABEL_BOTTOM);
        slRt.sizeDelta = new Vector2(-16f, SLOT_LABEL_HEIGHT);
        p.slotLabel = slotLabelGo.AddComponent<Text>();
        p.slotLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        p.slotLabel.alignment = TextAnchor.MiddleCenter;
        p.slotLabel.color = subtleTextColor;
        p.slotLabel.fontSize = 12;
        p.slotLabel.text = "";

        // Select button
        var btnGo = NewUIObject(go.transform, "SelectButton");
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0f, 0f);
        btnRt.anchorMax = new Vector2(1f, 0f);
        btnRt.pivot     = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0f, SELECT_BOTTOM);
        btnRt.sizeDelta = new Vector2(-32f, SELECT_HEIGHT);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = buttonColor;
        p.select = btnGo.AddComponent<Button>();
        var colors = p.select.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor = buttonPressedColor;
        colors.selectedColor = buttonHoverColor;
        p.select.colors = colors;

        var btnLblGo = NewUIObject(btnGo.transform, "Label");
        var lblRt = btnLblGo.GetComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = Vector2.zero; lblRt.offsetMax = Vector2.zero;
        var lbl = btnLblGo.AddComponent<Text>();
        lbl.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lbl.alignment = TextAnchor.MiddleCenter;
        lbl.color = textColor;
        lbl.fontSize = 18;
        lbl.fontStyle = FontStyle.Bold;
        lbl.text = "SELECT";

        return p;
    }

    private static GameObject NewUIObject(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}
