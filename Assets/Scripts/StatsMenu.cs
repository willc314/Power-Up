using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Procedural stats panel showing every Hero stat that powerup boosts touch,
/// with the snapshotted "original" value at the start of the run, the
/// current live value, and the delta between them.
///
/// Auto-spawned (DontDestroyOnLoad) so it survives across scenes alongside
/// the OptionsMenu. Triggered by the OptionsMenu's "Stats" button. ESC and
/// the Close button both hide it.
/// </summary>
public class StatsMenu : MonoBehaviour
{
    public static StatsMenu Instance { get; private set; }

    [Header("Style")]
    public Color overlayColor       = new Color(0f, 0f, 0f, 0.65f);
    public Color panelColor         = new Color(0.12f, 0.12f, 0.14f, 0.97f);
    public Color panelOutlineColor  = new Color(1f, 1f, 1f, 0.18f);
    public Color buttonColor        = new Color(0.22f, 0.22f, 0.26f, 1f);
    public Color buttonHoverColor   = new Color(0.32f, 0.34f, 0.40f, 1f);
    public Color buttonPressedColor = new Color(0.16f, 0.16f, 0.20f, 1f);
    public Color textColor          = Color.white;
    public Color subtleColor        = new Color(1f, 1f, 1f, 0.7f);
    public Color positiveDeltaColor = new Color(0.5f, 0.95f, 0.5f);  // green
    public Color negativeDeltaColor = new Color(0.95f, 0.55f, 0.55f); // red
    public Color neutralDeltaColor  = new Color(1f, 1f, 1f, 0.45f);  // grey

    private GameObject panelRoot;
    private Font defaultFont;

    // Per-row references so Refresh() can update text without rebuilding.
    private struct Row { public Text current; public Text delta; }
    private Row rowMaxHP, rowMove, rowRegen, rowDamageMul, rowCritRate, rowCritDmg, rowDashCd, rowIFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("StatsMenu (auto-spawned)");
        go.AddComponent<StatsMenu>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(false);

        EnsureEventSystem();
        BuildUI();
        Hide();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        // Live-refresh values while visible. Hide is driven by OptionsMenu.
        if (panelRoot != null && panelRoot.activeSelf) Refresh();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.hideFlags = HideFlags.DontSave;
    }

    public void Show()
    {
        if (panelRoot == null) return;
        if (!panelRoot.activeSelf) panelRoot.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // -------------------- Refresh values --------------------

    private void Refresh()
    {
        Hero h = Hero.Instance;
        if (h == null) return; // no live hero — show defaults from last build

        // Each row formats its current value and a colored delta.
        SetRow(rowMaxHP,     h.MaxHP,                     h.OriginalMaxHP,                "{0:0.#}",      "");
        SetRow(rowMove,      h.moveSpeed,                 h.OriginalMoveSpeed,            "{0:0.##}",     "");
        SetRow(rowRegen,     h.healthRegenPerSecond,      h.OriginalHealthRegen,          "{0:0.##}/s",   "");
        SetRow(rowDamageMul, h.damageMultiplier,          h.OriginalDamageMultiplier,     "{0:0.##}×",    "");
        SetRow(rowCritRate,  h.critRate * 100f,           h.OriginalCritRate * 100f,      "{0:0}%",       "");
        SetRow(rowCritDmg,   h.critDamage,                h.OriginalCritDamage,           "{0:0.##}×",    "");
        // Dash cooldown: shrinking is GOOD, so flip the delta sign for color logic.
        SetRow(rowDashCd,    h.dashCooldown,              h.OriginalDashCooldown,         "{0:0.##}s",    "", invertDelta: true);
        // i-frame window: dashDuration + extension, scaled by the multiplier.
        float baseWindow = h.dashDuration + Mathf.Max(0f, h.dashInvulnerabilityExtension);
        float currentWindow  = baseWindow * h.DashIFrameMultiplier;
        float originalWindow = baseWindow * h.OriginalDashIFrameMultiplier;
        SetRow(rowIFrames, currentWindow, originalWindow, "{0:0.000}s", "");
    }

    private void SetRow(Row row, float current, float original, string fmt, string suffix, bool invertDelta = false)
    {
        if (row.current == null) return;
        row.current.text = string.Format(fmt, current) + suffix;

        if (row.delta == null) return;
        float delta = current - original;
        // For metrics where smaller is better (e.g. cooldown), invert the
        // sign of the delta for color/+sign purposes.
        float effective = invertDelta ? -delta : delta;
        if (Mathf.Abs(delta) < 0.0001f)
        {
            row.delta.text = "—";
            row.delta.color = neutralDeltaColor;
        }
        else
        {
            string sign = delta > 0f ? "+" : "";
            row.delta.text = sign + string.Format(fmt, delta) + suffix;
            row.delta.color = effective > 0f ? positiveDeltaColor : negativeDeltaColor;
        }
    }

    // -------------------- UI construction --------------------

    private void BuildUI()
    {
        GameObject canvasGo = new GameObject("StatsCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 260; // above the OptionsMenu's overlay so the panel reads cleanly

        // Don't block clicks — the OptionsMenu handles input.
        canvasGo.GetComponent<GraphicRaycaster>().enabled = false;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Side panel anchored to the LEFT edge of the screen so it sits
        // alongside the centered OptionsMenu rather than overlapping. No dim
        // overlay — the OptionsMenu's overlay already handles screen darkening.
        GameObject panel = MakeUI("Panel", canvasGo.transform);
        var panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = new Vector2(0f, 0.5f);
        panelRt.anchorMax = new Vector2(0f, 0.5f);
        panelRt.pivot = new Vector2(0f, 0.5f);
        panelRt.anchoredPosition = new Vector2(40f, 0f); // 40 px from the left edge
        panelRt.sizeDelta = new Vector2(420f, 600f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = panelColor;
        panelImg.raycastTarget = false;
        AddOutline(panel, panelOutlineColor);

        panelRoot = panel; // Show/Hide toggles the side panel directly

        // Title — centered across the side panel.
        BuildText(panel.transform, "Title", "Hero Stats",
            anchorTop: true, x: 0f, y: -24f, width: 420f, height: 48f,
            fontSize: 30, color: textColor, align: TextAnchor.UpperCenter);

        // Column headers. Sized to fit the 420-wide panel:
        //   [   Stat   ] [ Current ] [   Δ   ]
        const float colName    = 16f;
        const float colCurrent = 220f;
        const float colDelta   = 330f;
        const float colCurW    = 100f;
        const float colDeltaW  = 80f;
        const float colNameW   = 200f;

        BuildText(panel.transform, "HdrStat",    "Stat",    anchorTop: true, x: colName,    y: -82f, width: colNameW,  height: 28f, fontSize: 18, color: subtleColor, align: TextAnchor.MiddleLeft);
        BuildText(panel.transform, "HdrCurrent", "Current", anchorTop: true, x: colCurrent, y: -82f, width: colCurW,   height: 28f, fontSize: 18, color: subtleColor, align: TextAnchor.MiddleRight);
        BuildText(panel.transform, "HdrDelta",   "Δ",       anchorTop: true, x: colDelta,   y: -82f, width: colDeltaW, height: 28f, fontSize: 18, color: subtleColor, align: TextAnchor.MiddleRight);

        // Stat rows. yStart leaves room below the header line.
        float y = -120f;
        const float rowH = 38f;
        rowMaxHP     = BuildStatRow(panel.transform, "Max HP",        ref y, rowH);
        rowMove      = BuildStatRow(panel.transform, "Move Speed",    ref y, rowH);
        rowRegen     = BuildStatRow(panel.transform, "Health Regen",  ref y, rowH);
        rowDamageMul = BuildStatRow(panel.transform, "Damage",        ref y, rowH);
        rowCritRate  = BuildStatRow(panel.transform, "Crit Rate",     ref y, rowH);
        rowCritDmg   = BuildStatRow(panel.transform, "Crit Damage",   ref y, rowH);
        rowDashCd    = BuildStatRow(panel.transform, "Dash Cooldown", ref y, rowH);
        rowIFrames   = BuildStatRow(panel.transform, "Dash i-frames", ref y, rowH);
    }

    /// <summary>Build a label in the Stat column and two values in Current/Δ columns.</summary>
    private Row BuildStatRow(Transform parent, string label, ref float y, float rowHeight)
    {
        const float colName    = 16f;
        const float colCurrent = 220f;
        const float colDelta   = 330f;
        const float colCurW    = 100f;
        const float colDeltaW  = 80f;
        const float colNameW   = 200f;

        BuildText(parent, "Lbl_" + label, label,
            anchorTop: true, x: colName, y: y, width: colNameW, height: rowHeight,
            fontSize: 18, color: textColor, align: TextAnchor.MiddleLeft);

        Text current = BuildText(parent, "Cur_" + label, "—",
            anchorTop: true, x: colCurrent, y: y, width: colCurW, height: rowHeight,
            fontSize: 18, color: textColor, align: TextAnchor.MiddleRight);

        Text delta = BuildText(parent, "Dlt_" + label, "—",
            anchorTop: true, x: colDelta, y: y, width: colDeltaW, height: rowHeight,
            fontSize: 18, color: neutralDeltaColor, align: TextAnchor.MiddleRight);

        y -= rowHeight + 2f;
        return new Row { current = current, delta = delta };
    }

    // -------------------- Helpers --------------------

    private GameObject MakeUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private Text BuildText(Transform parent, string name, string text,
        bool anchorTop = false, bool anchorBottom = false,
        float x = 0f, float y = 0f, float width = 200f, float height = 30f,
        int fontSize = 22, Color color = default, TextAnchor align = TextAnchor.MiddleLeft)
    {
        if (color == default) color = Color.white;
        GameObject go = MakeUI(name, parent);
        var rt = (RectTransform)go.transform;

        Vector2 amin, amax, pivot;
        if (anchorTop)         { amin = new Vector2(0f, 1f); amax = new Vector2(0f, 1f); pivot = new Vector2(0f, 1f); }
        else if (anchorBottom) { amin = new Vector2(0.5f, 0f); amax = new Vector2(0.5f, 0f); pivot = new Vector2(0.5f, 0f); }
        else                   { amin = new Vector2(0.5f, 0.5f); amax = new Vector2(0.5f, 0.5f); pivot = new Vector2(0.5f, 0.5f); }
        rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = pivot;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, height);

        var t = go.AddComponent<Text>();
        t.font = defaultFont;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = align;
        t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private GameObject BuildButton(Transform parent, string name,
        Vector2 anchoredPos, Vector2 size,
        out Button button, out Text label,
        bool anchorBottom = false)
    {
        GameObject go = MakeUI(name, parent);
        var rt = (RectTransform)go.transform;
        if (anchorBottom)
        {
            rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        }
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.color = buttonColor;
        button = go.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor = buttonPressedColor;
        colors.selectedColor = buttonHoverColor;
        button.colors = colors;

        GameObject lblGo = MakeUI("Label", go.transform);
        var lblRt = (RectTransform)lblGo.transform;
        lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(8f, 4f); lblRt.offsetMax = new Vector2(-8f, -4f);
        label = lblGo.AddComponent<Text>();
        label.font = defaultFont;
        label.fontSize = 22;
        label.color = textColor;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return go;
    }

    private void AddOutline(GameObject go, Color color)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(1f, -1f);
    }
}
