using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game victory overlay shown when the player kills the SlimeGod final
/// boss. Built procedurally — drop one GameObject named "WinMenu" into the
/// gameplay scene with this component on it. Hero.TriggerVictory pauses time
/// and calls <see cref="Show"/>, which inflates a centered panel with score
/// readouts and three buttons:
///
///   * Continue — closes the menu, unpauses time, calls
///                <see cref="GameManager.ContinueAfterVictory"/> and
///                <see cref="EnemySpawner.ScheduleNextFinalBoss"/> so the
///                next final boss spawns N more minutes later with the next
///                HP-scale step.
///   * Restart  — loads the gameplay scene fresh.
///   * Title    — loads the title scene.
///
/// All three buttons unfreeze the hero before transitioning so input/movement
/// don't stay locked if the player ends up here again.
/// </summary>
public class WinMenu : MonoBehaviour
{
    public static WinMenu Instance { get; private set; }

    [Header("Scenes")]
    [Tooltip("Scene name loaded by the Restart button.")]
    public string gameplaySceneName = "SampleScene";
    [Tooltip("Scene name loaded by the Title button.")]
    public string titleSceneName = "TitleScreen";

    [Header("Style")]
    public Color overlayColor       = new Color(0f, 0f, 0f, 0.6f);
    public Color panelColor         = new Color(0.10f, 0.12f, 0.10f, 0.96f);
    public Color panelOutlineColor  = new Color(0.5f, 1f, 0.7f, 0.45f);
    public Color titleColor         = new Color(0.5f, 1f, 0.7f, 1f);
    public Color textColor          = Color.white;
    public Color subtleColor        = new Color(1f, 1f, 1f, 0.78f);
    public Color highlightColor     = new Color(1f, 0.85f, 0.2f);
    public Color buttonColor        = new Color(0.20f, 0.28f, 0.22f, 1f);
    public Color buttonHoverColor   = new Color(0.30f, 0.42f, 0.34f, 1f);
    public Color buttonPressedColor = new Color(0.14f, 0.20f, 0.16f, 1f);
    public Color continueButtonColor = new Color(0.18f, 0.55f, 0.30f, 1f);

    [Header("Layout")]
    public int titleFontSize      = 84;
    public int finalScoreFontSize = 56;
    public int highScoreFontSize  = 32;
    public int newHighFontSize    = 36;
    public int buttonFontSize     = 30;
    public Vector2 panelSize      = new Vector2(720, 620);
    public Vector2 buttonSize     = new Vector2(280, 70);
    public int buttonSpacing      = 16;

    private Canvas canvas;
    private GameObject root;
    private Font defaultFont;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildUI();
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Show the win menu. Pauses time so the gameplay scene freezes underneath.
    /// </summary>
    public void Show()
    {
        if (root == null) BuildUI();
        RefreshScoreText();
        root.SetActive(true);
        Time.timeScale = 0f;

        // Swap to the victory track. Crossfades cleanly from boss music.
        if (MusicManager.Instance != null) MusicManager.Instance.PlayVictory();
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        Time.timeScale = 1f;
    }

    // -------------------- Button callbacks --------------------

    private void OnContinue()
    {
        Hide();

        // Unfreeze the hero so they can keep playing.
        if (Hero.Instance != null) Hero.Instance.ResumeFromVictoryPause();

        // GameManager: clear the "victory" flag so the run keeps going. The
        // bonus points already awarded stay in bonusPoints.
        if (GameManager.Instance != null) GameManager.Instance.ContinueAfterVictory();

        // EnemySpawner: schedule the next final boss with a new HP scale.
        if (EnemySpawner.Instance != null) EnemySpawner.Instance.ScheduleNextFinalBoss();

        // Resume normal arena music. (PlayBoss will be called again when the
        // next boss begins.)
        if (MusicManager.Instance != null) MusicManager.Instance.PlayBackground();
    }

    private void OnRestart()
    {
        // Make sure the next scene starts unpaused. The hero unfreeze is
        // moot since we're loading a new scene, but keep it for safety.
        if (Hero.Instance != null) Hero.Instance.ResumeFromVictoryPause();
        Time.timeScale = 1f;
        SceneManager.LoadScene(string.IsNullOrEmpty(gameplaySceneName) ? "SampleScene" : gameplaySceneName);
    }

    private void OnTitle()
    {
        if (Hero.Instance != null) Hero.Instance.ResumeFromVictoryPause();
        Time.timeScale = 1f;
        SceneManager.LoadScene(string.IsNullOrEmpty(titleSceneName) ? "TitleScreen" : titleSceneName);
    }

    // -------------------- UI construction --------------------

    private void BuildUI()
    {
        // Root canvas (overlay).
        GameObject canvasGo = new GameObject("WinMenuCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // sits above HUD

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        root = canvasGo;

        // Dim background overlay.
        GameObject overlay = MakeUIObject("Overlay", canvasGo.transform);
        var oRt = (RectTransform)overlay.transform;
        oRt.anchorMin = Vector2.zero; oRt.anchorMax = Vector2.one;
        oRt.offsetMin = Vector2.zero; oRt.offsetMax = Vector2.zero;
        var oImg = overlay.AddComponent<Image>();
        oImg.color = overlayColor;
        oImg.raycastTarget = true; // swallow clicks behind the menu

        // Centered panel.
        GameObject panel = MakeUIObject("Panel", canvasGo.transform);
        var pRt = (RectTransform)panel.transform;
        pRt.anchorMin = new Vector2(0.5f, 0.5f);
        pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.pivot     = new Vector2(0.5f, 0.5f);
        pRt.sizeDelta = panelSize;
        pRt.anchoredPosition = Vector2.zero;
        var pImg = panel.AddComponent<Image>();
        pImg.color = panelColor;
        var outline = panel.AddComponent<Outline>();
        outline.effectColor = panelOutlineColor;
        outline.effectDistance = new Vector2(3f, -3f);

        BuildPanelContents(panel.transform);
    }

    private void BuildPanelContents(Transform parent)
    {
        // Layout: Title at top, then NEW HIGH (optional), Score, Best, then 3 buttons.
        // We anchor each block to top-center of the panel and stack downward.
        float y = -32f; // distance from top of panel (negative = down)

        // Title.
        titleLabel = BuildLabel(parent, "Title", "★ VICTORY ★",
            anchorTopAtY: y, height: titleFontSize + 12,
            color: titleColor, fontSize: titleFontSize, bold: true);
        y -= titleFontSize + 12;

        // NEW HIGH (only shown when applicable).
        newHighLabel = BuildLabel(parent, "NewHigh", "★ NEW HIGH SCORE ★",
            anchorTopAtY: y, height: newHighFontSize + 8,
            color: highlightColor, fontSize: newHighFontSize, bold: true);
        y -= newHighFontSize + 8;

        // Score.
        scoreLabel = BuildLabel(parent, "Score", "Score: 0",
            anchorTopAtY: y - 8f, height: finalScoreFontSize + 12,
            color: textColor, fontSize: finalScoreFontSize, bold: true);
        y -= finalScoreFontSize + 12 + 8;

        // High Score.
        highScoreLabel = BuildLabel(parent, "Best", "Best: 0",
            anchorTopAtY: y, height: highScoreFontSize + 8,
            color: subtleColor, fontSize: highScoreFontSize, bold: false);
        y -= highScoreFontSize + 8;

        // Buttons (Continue, Restart, Title) stacked centered.
        y -= 32f;

        BuildButton(parent, "ContinueButton", "Continue",
            anchorTopAtY: y, color: continueButtonColor,
            onClick: OnContinue);
        y -= buttonSize.y + buttonSpacing;

        BuildButton(parent, "RestartButton", "Restart",
            anchorTopAtY: y, color: buttonColor,
            onClick: OnRestart);
        y -= buttonSize.y + buttonSpacing;

        BuildButton(parent, "TitleButton", "Title",
            anchorTopAtY: y, color: buttonColor,
            onClick: OnTitle);
    }

    private Text titleLabel;
    private Text newHighLabel;
    private Text scoreLabel;
    private Text highScoreLabel;

    private void RefreshScoreText()
    {
        int finalScore = GameManager.Instance != null ? GameManager.Instance.Score : 0;
        int highScore  = GameManager.Instance != null ? GameManager.Instance.HighScore : 0;
        bool newHigh   = GameManager.Instance != null && GameManager.Instance.NewHighScoreThisRun;

        if (scoreLabel    != null) scoreLabel.text    = "Score:  " + finalScore;
        if (highScoreLabel != null) highScoreLabel.text = "Best:   " + highScore;
        if (newHighLabel  != null) newHighLabel.gameObject.SetActive(newHigh);
    }

    // -------------------- UI helpers --------------------

    private GameObject MakeUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private Text BuildLabel(Transform parent, string name, string text,
        float anchorTopAtY, float height, Color color, int fontSize, bool bold)
    {
        GameObject go = MakeUIObject(name, parent);
        var rt = (RectTransform)go.transform;
        // Anchor to top-center of the panel; anchorTopAtY < 0 = down from top.
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, anchorTopAtY);
        rt.sizeDelta = new Vector2(0f, height);

        var t = go.AddComponent<Text>();
        t.font = defaultFont;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAnchor.UpperCenter;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.text = text;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    private void BuildButton(Transform parent, string name, string label,
        float anchorTopAtY, Color color, System.Action onClick)
    {
        GameObject go = MakeUIObject(name, parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, anchorTopAtY);
        rt.sizeDelta = buttonSize;

        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor    = color;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor   = buttonPressedColor;
        colors.selectedColor  = buttonHoverColor;
        colors.disabledColor  = new Color(color.r, color.g, color.b, 0.35f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick?.Invoke());

        // Button label.
        GameObject txtGo = MakeUIObject("Label", go.transform);
        var trt = (RectTransform)txtGo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var t = txtGo.AddComponent<Text>();
        t.font = defaultFont;
        t.fontSize = buttonFontSize;
        t.color = textColor;
        t.alignment = TextAnchor.MiddleCenter;
        t.fontStyle = FontStyle.Bold;
        t.text = label;
        t.raycastTarget = false;

        var outline = txtGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }
}
