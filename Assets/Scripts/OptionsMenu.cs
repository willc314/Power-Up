using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Procedural options menu. Three rows:
///   * FPS Limit       — pick from a list of caps (or Unlimited).
///   * Resolution      — pick from common 16:9 sizes.
///   * Fullscreen      — Yes / No.
///
/// Each row is a horizontal strip of buttons; the currently-selected button
/// is highlighted. Settings save and apply immediately on click via
/// GameSettings, so the player sees the change right away.
///
/// Setup:
///   1. Drop one empty GameObject named "OptionsMenu" in the scene with this
///      component (typically your main menu scene).
///   2. From a Button's OnClick, call OptionsMenu.Instance.Show().
///      MainMenu.OpenOptions() is a convenience wrapper.
///   3. Press ESC at any time to toggle the panel.
/// </summary>
public class OptionsMenu : MonoBehaviour
{
    public static OptionsMenu Instance { get; private set; }

    [Header("Style")]
    public Color overlayColor       = new Color(0f, 0f, 0f, 0.65f);
    public Color panelColor         = new Color(0.12f, 0.12f, 0.14f, 0.97f);
    public Color panelOutlineColor  = new Color(1f, 1f, 1f, 0.18f);
    public Color buttonColor        = new Color(0.22f, 0.22f, 0.26f, 1f);
    public Color buttonHoverColor   = new Color(0.32f, 0.34f, 0.40f, 1f);
    public Color buttonPressedColor = new Color(0.16f, 0.16f, 0.20f, 1f);
    public Color buttonSelectedColor= new Color(0.42f, 0.55f, 0.85f, 1f);
    public Color textColor          = Color.white;
    public Color subtleTextColor    = new Color(1f, 1f, 1f, 0.7f);

    [Tooltip("Key that toggles the options panel from anywhere.")]
    public KeyCode toggleKey = KeyCode.Escape;

    private GameObject panelRoot;
    private Font defaultFont;

    // Per-row state
    private struct OptionButton { public Button button; public Image image; public int valueKey; }
    private readonly List<OptionButton> fullscreenButtons = new List<OptionButton>();
    // The FPS slider owns its own selection state; we just keep references so
    // Refresh() can pull values back in if settings change externally.
    private Slider fpsSlider;
    private Text   fpsValueLabel;
    private Dropdown resolutionDropdown;

    private void Awake()
    {
        Instance = this;
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Hide any leftover children from earlier scene-side wiring.
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null) child.gameObject.SetActive(false);
        }

        EnsureEventSystem();
        BuildUI();
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.hideFlags = HideFlags.DontSave;
    }

    public void Toggle()
    {
        if (panelRoot == null) return;
        if (panelRoot.activeSelf) Hide();
        else Show();
    }

    public void Show()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Refresh()
    {
        var s = GameSettings.Instance;
        if (s == null) return;

        if (fpsSlider != null)
        {
            int idx = System.Array.IndexOf(GameSettings.FpsOptions, s.TargetFps);
            if (idx < 0) idx = 1; // default to 60 FPS option
            fpsSlider.SetValueWithoutNotify(idx);
            if (fpsValueLabel != null) fpsValueLabel.text = GameSettings.FormatFps(s.TargetFps);
        }

        if (resolutionDropdown != null)
        {
            int rIdx = 0;
            for (int i = 0; i < GameSettings.ResolutionOptions.Length; i++)
                if (GameSettings.ResolutionOptions[i] == s.Resolution) { rIdx = i; break; }
            resolutionDropdown.SetValueWithoutNotify(rIdx);
            resolutionDropdown.RefreshShownValue();
        }

        HighlightSelection(fullscreenButtons, s.Fullscreen ? 1 : 0);
    }

    private void HighlightSelection(List<OptionButton> row, int selectedKey)
    {
        for (int i = 0; i < row.Count; i++)
        {
            bool selected = row[i].valueKey == selectedKey;
            var colors = row[i].button.colors;
            colors.normalColor      = selected ? buttonSelectedColor : buttonColor;
            colors.highlightedColor = selected ? buttonSelectedColor : buttonHoverColor;
            colors.selectedColor    = selected ? buttonSelectedColor : buttonHoverColor;
            colors.pressedColor     = buttonPressedColor;
            colors.colorMultiplier  = 1f;
            row[i].button.colors = colors;
            // Force-refresh the displayed color (Button only updates on state change).
            if (row[i].image != null) row[i].image.color = selected ? buttonSelectedColor : buttonColor;
        }
    }

    // -------------------- UI construction --------------------

    private void BuildUI()
    {
        // Root canvas covering the screen.
        GameObject canvasGo = new GameObject("OptionsCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250; // above the gameplay HUD and powerup panel

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Dim full-screen background (catches clicks).
        GameObject overlay = MakeUI("Overlay", canvasGo.transform);
        var overlayRt = (RectTransform)overlay.transform;
        overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
        var overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = overlayColor;
        overlayImg.raycastTarget = true;
        panelRoot = overlay;

        // Centered panel
        GameObject panel = MakeUI("Panel", overlay.transform);
        var panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(960f, 600f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = panelColor;
        panelImg.raycastTarget = true;
        AddOutline(panel, panelOutlineColor);

        // Title at the top
        BuildLabel(panel.transform, "Options",
            new Vector2(0f, -28f), new Vector2(900f, 60f),
            anchor: TextAnchor.UpperCenter, fontSize: 44, color: textColor,
            anchorTop: true);

        // --- Row: FPS Limit ---
        BuildLabel(panel.transform, "FPS Limit",
            new Vector2(40f, -120f), new Vector2(280f, 40f),
            anchor: TextAnchor.MiddleLeft, fontSize: 26, color: subtleTextColor,
            anchorTop: true);
        BuildOptionRowFps(panel.transform, /*yFromTop*/ -120f);

        // --- Row: Resolution ---
        BuildLabel(panel.transform, "Resolution",
            new Vector2(40f, -240f), new Vector2(280f, 40f),
            anchor: TextAnchor.MiddleLeft, fontSize: 26, color: subtleTextColor,
            anchorTop: true);
        BuildOptionRowResolution(panel.transform, /*yFromTop*/ -240f);

        // --- Row: Fullscreen ---
        BuildLabel(panel.transform, "Fullscreen",
            new Vector2(40f, -360f), new Vector2(280f, 40f),
            anchor: TextAnchor.MiddleLeft, fontSize: 26, color: subtleTextColor,
            anchorTop: true);
        BuildOptionRowFullscreen(panel.transform, /*yFromTop*/ -360f);

        // Close button at the bottom
        var closeGo = BuildButton(panel.transform, "Close",
            new Vector2(0f, 40f), new Vector2(220f, 60f),
            out Button closeBtn, out Text closeText, anchorBottom: true);
        closeText.text = "Close";
        closeText.fontSize = 24;
        closeBtn.onClick.AddListener(Hide);
    }

    private void BuildOptionRowFps(Transform parent, float yFromTop)
    {
        // FPS row: a horizontal Slider with whole-number snaps. The slider's
        // value is an index into GameSettings.FpsOptions, and a live label
        // beside it shows the current FPS value (or "Unlimited" for index 0
        // of the list's special end).
        const float startX     = 320f;
        const float sliderW    = 440f;
        const float sliderH    = 30f;
        const float labelW     = 160f;
        const float labelGap   = 18f;

        int currentFps = GameSettings.Instance != null ? GameSettings.Instance.TargetFps : 60;
        int currentIdx = System.Array.IndexOf(GameSettings.FpsOptions, currentFps);
        if (currentIdx < 0) currentIdx = 1;

        fpsSlider = BuildSlider(parent,
            new Vector2(startX, yFromTop - 10f),
            new Vector2(sliderW, sliderH),
            min: 0, max: GameSettings.FpsOptions.Length - 1, wholeNumbers: true,
            initialValue: currentIdx);

        // Live value label
        GameObject labelGo = MakeUI("FPS_Value", parent);
        var lblRt = (RectTransform)labelGo.transform;
        lblRt.anchorMin = new Vector2(0f, 1f);
        lblRt.anchorMax = new Vector2(0f, 1f);
        lblRt.pivot = new Vector2(0f, 1f);
        lblRt.anchoredPosition = new Vector2(startX + sliderW + labelGap, yFromTop);
        lblRt.sizeDelta = new Vector2(labelW, 40f);
        fpsValueLabel = labelGo.AddComponent<Text>();
        fpsValueLabel.font = defaultFont;
        fpsValueLabel.fontSize = 22;
        fpsValueLabel.color = textColor;
        fpsValueLabel.alignment = TextAnchor.MiddleLeft;
        fpsValueLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        fpsValueLabel.text = GameSettings.FormatFps(currentFps);
        fpsValueLabel.raycastTarget = false;

        fpsSlider.onValueChanged.AddListener(v =>
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(v), 0, GameSettings.FpsOptions.Length - 1);
            int fps = GameSettings.FpsOptions[idx];
            GameSettings.Instance.SetTargetFps(fps);
            if (fpsValueLabel != null) fpsValueLabel.text = GameSettings.FormatFps(fps);
        });
    }

    private void BuildOptionRowResolution(Transform parent, float yFromTop)
    {
        // Resolution row: a Unity Dropdown built from scratch. The dropdown's
        // expanded panel uses ScrollRect+Mask so it scrolls if the screen is
        // small, but in practice the resolution list is short enough to fit.
        const float startX = 320f;
        const float ddW    = 240f;
        const float ddH    = 50f;

        var optionLabels = new List<string>();
        int initialIdx = 0;
        Vector2Int currentRes = GameSettings.Instance != null ? GameSettings.Instance.Resolution : new Vector2Int(1920, 1080);
        for (int i = 0; i < GameSettings.ResolutionOptions.Length; i++)
        {
            Vector2Int res = GameSettings.ResolutionOptions[i];
            optionLabels.Add(GameSettings.FormatResolution(res));
            if (res == currentRes) initialIdx = i;
        }

        resolutionDropdown = BuildDropdown(parent,
            new Vector2(startX, yFromTop - 5f),
            new Vector2(ddW, ddH),
            optionLabels,
            initialIdx);

        resolutionDropdown.onValueChanged.AddListener(idx =>
        {
            if (idx < 0 || idx >= GameSettings.ResolutionOptions.Length) return;
            GameSettings.Instance.SetResolution(GameSettings.ResolutionOptions[idx]);
        });
    }

    private void BuildOptionRowFullscreen(Transform parent, float yFromTop)
    {
        fullscreenButtons.Clear();
        const float startX = 320f;
        const float btnW   = 100f;
        const float btnH   = 50f;
        const float gap    = 10f;

        for (int i = 0; i < 2; i++)
        {
            bool isYes = i == 1;
            float x = startX + (btnW + gap) * i;
            var btnGo = BuildButton(parent, isYes ? "FS_Yes" : "FS_No",
                new Vector2(x, yFromTop), new Vector2(btnW, btnH),
                out Button b, out Text t, anchorTop: true, anchorLeft: true);
            t.text = isYes ? "Yes" : "No";
            t.fontSize = 20;

            bool captured = isYes;
            b.onClick.AddListener(() =>
            {
                GameSettings.Instance.SetFullscreen(captured);
                Refresh();
            });
            fullscreenButtons.Add(new OptionButton { button = b, image = btnGo.GetComponent<Image>(), valueKey = isYes ? 1 : 0 });
        }
    }

    private static int EncodeResolution(Vector2Int r) => r.x * 10000 + r.y;

    // -------------------- Helpers --------------------

    private GameObject MakeUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private void BuildLabel(Transform parent, string text,
        Vector2 anchoredPos, Vector2 size,
        TextAnchor anchor, int fontSize, Color color,
        bool anchorTop = false, bool anchorBottom = false, bool anchorLeft = false)
    {
        GameObject go = MakeUI("Label_" + text, parent);
        var rt = (RectTransform)go.transform;
        Vector2 amin, amax, pivot;
        if (anchorTop)         { amin = new Vector2(0f, 1f); amax = new Vector2(0f, 1f); pivot = new Vector2(0f, 1f); }
        else if (anchorBottom) { amin = new Vector2(0.5f, 0f); amax = new Vector2(0.5f, 0f); pivot = new Vector2(0.5f, 0f); }
        else                   { amin = new Vector2(0.5f, 0.5f); amax = new Vector2(0.5f, 0.5f); pivot = new Vector2(0.5f, 0.5f); }
        rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var t = go.AddComponent<Text>();
        t.font = defaultFont;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = anchor;
        t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
    }

    private GameObject BuildButton(Transform parent, string name,
        Vector2 anchoredPos, Vector2 size,
        out Button button, out Text label,
        bool anchorTop = false, bool anchorBottom = false, bool anchorLeft = false)
    {
        GameObject go = MakeUI(name, parent);
        var rt = (RectTransform)go.transform;
        Vector2 amin, amax, pivot;
        if (anchorTop && anchorLeft) { amin = new Vector2(0f, 1f); amax = new Vector2(0f, 1f); pivot = new Vector2(0f, 1f); }
        else if (anchorTop)          { amin = new Vector2(0.5f, 1f); amax = new Vector2(0.5f, 1f); pivot = new Vector2(0.5f, 1f); }
        else if (anchorBottom)       { amin = new Vector2(0.5f, 0f); amax = new Vector2(0.5f, 0f); pivot = new Vector2(0.5f, 0f); }
        else                         { amin = new Vector2(0.5f, 0.5f); amax = new Vector2(0.5f, 0.5f); pivot = new Vector2(0.5f, 0.5f); }
        rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.color = buttonColor;
        img.raycastTarget = true;

        button = go.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor      = buttonColor;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor     = buttonPressedColor;
        colors.selectedColor    = buttonHoverColor;
        colors.colorMultiplier  = 1f;
        button.colors = colors;

        GameObject lblGo = MakeUI("Label", go.transform);
        var lblRt = (RectTransform)lblGo.transform;
        lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(6f, 4f);
        lblRt.offsetMax = new Vector2(-6f, -4f);
        label = lblGo.AddComponent<Text>();
        label.font = defaultFont;
        label.fontSize = 18;
        label.color = textColor;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.raycastTarget = false;

        return go;
    }

    private void AddOutline(GameObject go, Color color)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(1f, -1f);
    }

    // -------------------- Slider builder --------------------

    /// <summary>
    /// Build a Unity UI Slider procedurally. Returns the Slider component.
    /// Background bar is dim, the fill highlights the selected portion, and
    /// the handle is a small white square the user drags.
    /// </summary>
    private Slider BuildSlider(Transform parent, Vector2 anchoredPos, Vector2 size,
        float min, float max, bool wholeNumbers, float initialValue)
    {
        GameObject sliderGo = MakeUI("Slider", parent);
        var sliderRt = (RectTransform)sliderGo.transform;
        sliderRt.anchorMin = new Vector2(0f, 1f);
        sliderRt.anchorMax = new Vector2(0f, 1f);
        sliderRt.pivot = new Vector2(0f, 1f);
        sliderRt.anchoredPosition = anchoredPos;
        sliderRt.sizeDelta = size;

        Slider slider = sliderGo.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;

        // Background track
        GameObject bgGo = MakeUI("Background", sliderGo.transform);
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = new Vector2(0f, 0.3f);
        bgRt.anchorMax = new Vector2(1f, 0.7f);
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.55f);
        bgImg.raycastTarget = true;

        // Fill area + Fill (the colored portion left of the handle)
        GameObject fillAreaGo = MakeUI("Fill Area", sliderGo.transform);
        var fillAreaRt = (RectTransform)fillAreaGo.transform;
        fillAreaRt.anchorMin = new Vector2(0f, 0.3f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.7f);
        fillAreaRt.offsetMin = new Vector2(5f, 0f);
        fillAreaRt.offsetMax = new Vector2(-15f, 0f);

        GameObject fillGo = MakeUI("Fill", fillAreaGo.transform);
        var fillRt = (RectTransform)fillGo.transform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.color = buttonSelectedColor;
        fillImg.raycastTarget = false;

        // Handle
        GameObject handleAreaGo = MakeUI("Handle Slide Area", sliderGo.transform);
        var handleAreaRt = (RectTransform)handleAreaGo.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(10f, 0f);
        handleAreaRt.offsetMax = new Vector2(-10f, 0f);

        GameObject handleGo = MakeUI("Handle", handleAreaGo.transform);
        var handleRt = (RectTransform)handleGo.transform;
        handleRt.sizeDelta = new Vector2(20f, 0f); // height stretches via anchors below
        handleRt.anchorMin = new Vector2(0f, 0f);
        handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        var handleImg = handleGo.AddComponent<Image>();
        handleImg.color = textColor;
        handleImg.raycastTarget = true;
        AddOutline(handleGo, new Color(0f, 0f, 0f, 0.4f));

        slider.fillRect    = fillRt;
        slider.handleRect  = handleRt;
        slider.targetGraphic = handleImg;
        slider.minValue    = min;
        slider.maxValue    = max;
        slider.wholeNumbers= wholeNumbers;
        slider.value       = initialValue;
        return slider;
    }

    // -------------------- Dropdown builder --------------------

    /// <summary>
    /// Build a Unity UI Dropdown procedurally. Returns the Dropdown component.
    /// The expanded panel is contained in a ScrollRect, so very long lists
    /// scroll instead of overflowing. The caller wires up onValueChanged.
    /// </summary>
    private Dropdown BuildDropdown(Transform parent, Vector2 anchoredPos, Vector2 size,
        List<string> options, int initialIndex)
    {
        GameObject ddGo = MakeUI("Dropdown", parent);
        var ddRt = (RectTransform)ddGo.transform;
        ddRt.anchorMin = new Vector2(0f, 1f);
        ddRt.anchorMax = new Vector2(0f, 1f);
        ddRt.pivot = new Vector2(0f, 1f);
        ddRt.anchoredPosition = anchoredPos;
        ddRt.sizeDelta = size;

        var ddImg = ddGo.AddComponent<Image>();
        ddImg.color = buttonColor;
        ddImg.raycastTarget = true;

        var dd = ddGo.AddComponent<Dropdown>();
        dd.targetGraphic = ddImg;
        var ddColors = dd.colors;
        ddColors.normalColor      = buttonColor;
        ddColors.highlightedColor = buttonHoverColor;
        ddColors.pressedColor     = buttonPressedColor;
        ddColors.selectedColor    = buttonHoverColor;
        ddColors.colorMultiplier  = 1f;
        dd.colors = ddColors;

        // Caption (currently-selected text shown on the closed dropdown)
        GameObject captionGo = MakeUI("Label", ddGo.transform);
        var capRt = (RectTransform)captionGo.transform;
        capRt.anchorMin = Vector2.zero; capRt.anchorMax = Vector2.one;
        capRt.offsetMin = new Vector2(14f, 6f);
        capRt.offsetMax = new Vector2(-32f, -6f);
        var captionText = captionGo.AddComponent<Text>();
        captionText.font = defaultFont;
        captionText.fontSize = 20;
        captionText.color = textColor;
        captionText.alignment = TextAnchor.MiddleLeft;
        captionText.raycastTarget = false;
        dd.captionText = captionText;

        // Arrow indicator (just a unicode chevron)
        GameObject arrowGo = MakeUI("Arrow", ddGo.transform);
        var arrowRt = (RectTransform)arrowGo.transform;
        arrowRt.anchorMin = new Vector2(1f, 0.5f);
        arrowRt.anchorMax = new Vector2(1f, 0.5f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.anchoredPosition = new Vector2(-10f, 0f);
        arrowRt.sizeDelta = new Vector2(20f, 20f);
        var arrowText = arrowGo.AddComponent<Text>();
        arrowText.font = defaultFont;
        arrowText.fontSize = 14;
        arrowText.color = subtleTextColor;
        arrowText.alignment = TextAnchor.MiddleCenter;
        arrowText.text = "▼";
        arrowText.raycastTarget = false;

        // Template — the panel that pops up when you click the dropdown.
        // Anchored to the bottom of the dropdown so it expands downward.
        GameObject templateGo = MakeUI("Template", ddGo.transform);
        var templateRt = (RectTransform)templateGo.transform;
        templateRt.anchorMin = new Vector2(0f, 0f);
        templateRt.anchorMax = new Vector2(1f, 0f);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0f, 2f);
        templateRt.sizeDelta = new Vector2(0f, 200f);
        var templateImg = templateGo.AddComponent<Image>();
        templateImg.color = panelColor;
        AddOutline(templateGo, panelOutlineColor);
        var scroll = templateGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // Viewport with a Mask so item rows clip cleanly.
        GameObject viewportGo = MakeUI("Viewport", templateGo.transform);
        var vpRt = (RectTransform)viewportGo.transform;
        vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
        var vpImg = viewportGo.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.02f);
        vpImg.raycastTarget = true;
        var mask = viewportGo.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Content holder
        GameObject contentGo = MakeUI("Content", viewportGo.transform);
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 32f);

        scroll.viewport = vpRt;
        scroll.content  = contentRt;

        // Item template (one row of the dropdown list)
        GameObject itemGo = MakeUI("Item", contentGo.transform);
        var itemRt = (RectTransform)itemGo.transform;
        itemRt.anchorMin = new Vector2(0f, 0.5f);
        itemRt.anchorMax = new Vector2(1f, 0.5f);
        itemRt.pivot = new Vector2(0.5f, 0.5f);
        itemRt.anchoredPosition = Vector2.zero;
        itemRt.sizeDelta = new Vector2(0f, 32f);
        var itemToggle = itemGo.AddComponent<Toggle>();
        itemToggle.transition = Selectable.Transition.ColorTint;
        var itemColors = itemToggle.colors;
        itemColors.normalColor      = buttonColor;
        itemColors.highlightedColor = buttonHoverColor;
        itemColors.pressedColor     = buttonPressedColor;
        itemColors.selectedColor    = buttonHoverColor;
        itemColors.colorMultiplier  = 1f;
        itemToggle.colors = itemColors;

        // Item Background (the colored row, also Toggle.targetGraphic)
        GameObject itemBgGo = MakeUI("Item Background", itemGo.transform);
        var itemBgRt = (RectTransform)itemBgGo.transform;
        itemBgRt.anchorMin = Vector2.zero; itemBgRt.anchorMax = Vector2.one;
        itemBgRt.offsetMin = Vector2.zero; itemBgRt.offsetMax = Vector2.zero;
        var itemBgImg = itemBgGo.AddComponent<Image>();
        itemBgImg.color = buttonColor;
        itemBgImg.raycastTarget = true;
        itemToggle.targetGraphic = itemBgImg;

        // Item Checkmark (shown on the currently-selected row)
        GameObject itemCheckmarkGo = MakeUI("Item Checkmark", itemGo.transform);
        var checkmarkRt = (RectTransform)itemCheckmarkGo.transform;
        checkmarkRt.anchorMin = new Vector2(0f, 0.5f);
        checkmarkRt.anchorMax = new Vector2(0f, 0.5f);
        checkmarkRt.pivot = new Vector2(0f, 0.5f);
        checkmarkRt.anchoredPosition = new Vector2(8f, 0f);
        checkmarkRt.sizeDelta = new Vector2(20f, 20f);
        var checkmarkImg = itemCheckmarkGo.AddComponent<Image>();
        checkmarkImg.color = buttonSelectedColor;
        checkmarkImg.raycastTarget = false;
        itemToggle.graphic = checkmarkImg;

        // Item Label
        GameObject itemLabelGo = MakeUI("Item Label", itemGo.transform);
        var itemLblRt = (RectTransform)itemLabelGo.transform;
        itemLblRt.anchorMin = Vector2.zero; itemLblRt.anchorMax = Vector2.one;
        itemLblRt.offsetMin = new Vector2(36f, 1f);
        itemLblRt.offsetMax = new Vector2(-10f, -2f);
        var itemLabelText = itemLabelGo.AddComponent<Text>();
        itemLabelText.font = defaultFont;
        itemLabelText.fontSize = 18;
        itemLabelText.color = textColor;
        itemLabelText.alignment = TextAnchor.MiddleLeft;
        itemLabelText.raycastTarget = false;

        dd.template  = templateRt;
        dd.itemText  = itemLabelText;

        // Hide the template by default — Unity's Dropdown enables it on click.
        templateGo.SetActive(false);

        // Populate options
        dd.options.Clear();
        foreach (string opt in options)
            dd.options.Add(new Dropdown.OptionData(opt));
        dd.value = Mathf.Clamp(initialIndex, 0, Mathf.Max(0, dd.options.Count - 1));
        dd.RefreshShownValue();

        return dd;
    }
}
