using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// End-screen menu. Provides scene-load buttons (wired up in the Inspector
/// via OnClick) and builds an optional procedural score readout that shows
/// the final score and high score, plus a "NEW HIGH SCORE!" line if the
/// just-finished run beat the previous best.
///
/// The procedural overlay can be disabled if you'd rather drive your own
/// scene-side Text or TMP_Text — in that case populate <see cref="finalScoreText"/>
/// and <see cref="highScoreText"/> in the Inspector and the script will write
/// the values into them instead.
/// </summary>
public class GameOverMenu : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Name of the gameplay scene to restart.")]
    public string gameplaySceneName = "SampleScene";

    [Tooltip("Name of the title screen scene.")]
    public string titleSceneName = "TitleScreen";

    [Header("Score Display")]
    [Tooltip("If true, builds a procedural score panel at the top of the EndScreen showing final score, high score, and a 'NEW HIGH SCORE!' line if applicable.")]
    public bool buildProceduralOverlay = true;

    [Tooltip("Optional Text component to populate with the final score. Used regardless of the procedural overlay setting.")]
    public Text finalScoreText;
    [Tooltip("Optional Text component to populate with the high score.")]
    public Text highScoreText;

    [Header("Procedural Overlay Style")]
    public Color textColor      = Color.white;
    public Color subtleColor    = new Color(1f, 1f, 1f, 0.75f);
    public Color highlightColor = new Color(1f, 0.85f, 0.2f);
    public Color victoryColor   = new Color(0.4f, 1f, 0.6f);
    public int   finalScoreFontSize = 64;
    public int   highScoreFontSize  = 36;
    public int   newHighFontSize    = 40;
    public int   victoryFontSize    = 96;
    [Tooltip("Vertical offset applied to the centered procedural readout. Positive = up. Use this to move the score / victory text above center so it doesn't sit on top of the menu buttons.")]
    public float overlayVerticalOffset = 280f;

    private void Start()
    {
        // Play the end-screen music if the scene has a MusicManager with one set up.
        if (MusicManager.Instance != null) MusicManager.Instance.PlayEnd();

        int finalScore = GameManager.LoadLastFinalScore();
        int highScore  = GameManager.LoadStoredHighScore();
        bool newHigh   = GameManager.LoadLastRunWasNewHigh();
        bool victory   = GameManager.LoadLastRunWasWin();

        // Optional inspector-wired text fields — useful if you've designed
        // your own EndScreen layout and just want the data populated.
        if (finalScoreText != null) finalScoreText.text = "Score: "       + finalScore;
        if (highScoreText  != null) highScoreText.text  = "High Score: "  + highScore;

        if (buildProceduralOverlay) BuildOverlay(finalScore, highScore, newHigh, victory);
    }

    private void BuildOverlay(int finalScore, int highScore, bool newHigh, bool victory)
    {
        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasGo = new GameObject("GameOverScoreCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // sit between the EndScreen background and any buttons

        // Don't block clicks on the buttons that may be behind us.
        canvasGo.GetComponent<GraphicRaycaster>().enabled = false;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Stack: (optional) VICTORY → (optional) NEW HIGH SCORE → Final Score → High Score.
        // Centered vertically — compute the total stack height, then place
        // each line so the whole block sits in the middle of the screen.
        const float spacing = 18f;
        float totalHeight =
              (victory ? victoryFontSize + spacing : 0f)
            + (newHigh ? newHighFontSize + spacing : 0f)
            + finalScoreFontSize + spacing
            + highScoreFontSize;

        // Top of the stack relative to canvas center, shifted up by the
        // overlay vertical offset so the readout sits in the upper half of
        // the screen instead of dead-center on top of the menu buttons.
        float y = totalHeight * 0.5f + overlayVerticalOffset;

        if (victory)
        {
            BuildLine(canvasGo.transform, "Victory",
                "★ VICTORY ★",
                anchoredY: y, height: victoryFontSize + 16,
                color: victoryColor, fontSize: victoryFontSize,
                font: defaultFont, bold: true);
            y -= victoryFontSize + spacing;
        }

        if (newHigh)
        {
            BuildLine(canvasGo.transform, "NewHigh",
                "★ NEW HIGH SCORE ★",
                anchoredY: y, height: newHighFontSize + 12,
                color: highlightColor, fontSize: newHighFontSize,
                font: defaultFont, bold: true);
            y -= newHighFontSize + spacing;
        }

        BuildLine(canvasGo.transform, "FinalScore",
            "Score:  " + finalScore,
            anchoredY: y, height: finalScoreFontSize + 16,
            color: textColor, fontSize: finalScoreFontSize,
            font: defaultFont, bold: true);
        y -= finalScoreFontSize + spacing;

        BuildLine(canvasGo.transform, "HighScore",
            "Best:   " + highScore,
            anchoredY: y, height: highScoreFontSize + 12,
            color: subtleColor, fontSize: highScoreFontSize,
            font: defaultFont, bold: false);
    }

    private static void BuildLine(Transform parent, string name, string text,
        float anchoredY, float height,
        Color color, int fontSize, Font font, bool bold)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        // Center-anchored with a top pivot. anchoredY is the rect's TOP edge,
        // measured upward from canvas center — so the caller can stack lines
        // by subtracting each line's height to get the next top.
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, anchoredY);
        rt.sizeDelta = new Vector2(900f, height);

        var t = go.AddComponent<Text>();
        t.font = font;
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
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void GoToTitleScreen()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }
}
