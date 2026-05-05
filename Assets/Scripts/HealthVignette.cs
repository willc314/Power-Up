using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen red vignette that fades in when the hero drops to critical
/// health and fades out when they're patched up again. Built entirely from
/// script so the only setup is one empty GameObject with this component in
/// the gameplay scene.
///
/// How it works:
///   * Generates a radial-gradient sprite once in Awake (transparent in the
///     middle, opaque toward the edges) so we don't depend on any imported
///     assets.
///   * Stretches an Image of that sprite over the whole screen (aspect is
///     not preserved → the gradient becomes ellipse-shaped, which is what
///     edge vignettes typically look like).
///   * Each frame, reads Hero.Instance.CurrentHP and lerps the Image's alpha
///     toward a target based on how far HP is below criticalHealth.
///   * Optional subtle pulse for extra urgency.
/// </summary>
public class HealthVignette : MonoBehaviour
{
    /// <summary>
    /// Singleton accessor so Hero.TakeDamage (and anything else that wants
    /// to flash the screen red on a hit) can call <see cref="Flash"/> without
    /// chasing a scene reference.
    /// </summary>
    public static HealthVignette Instance { get; private set; }

    [Header("Threshold")]
    [Tooltip("HP at which the vignette is fully invisible. Below this, the alpha ramps up to maxAlpha as HP approaches 0.")]
    public float criticalHealth = 20f;

    [Header("Style")]
    [Tooltip("Color of the vignette. Alpha is overridden each frame from HP — the alpha you set here is ignored.")]
    public Color vignetteColor = new Color(0.85f, 0.07f, 0.07f, 1f);
    [Tooltip("Maximum alpha the vignette ever reaches (at HP = 0).")]
    [Range(0f, 1f)] public float maxAlpha = 0.65f;
    [Tooltip("How quickly the vignette fades in/out, in alpha units per second.")]
    public float fadeSpeed = 3.5f;

    [Header("Hit Flash")]
    [Tooltip("Peak alpha of the brief red flash whenever the hero takes damage. Stacks ON TOP of the low-HP vignette so a hit at full HP still reads, and a hit at low HP gets even more dramatic. 0 = disabled.")]
    [Range(0f, 1f)] public float hitFlashPeakAlpha = 0.3f;
    [Tooltip("Seconds the hit-flash takes to fade from peak back to zero.")]
    public float hitFlashFadeTime = 0.35f;

    [Header("Pulse (optional)")]
    [Tooltip("Pulses per second while the vignette is visible. 0 = no pulse.")]
    public float pulseSpeed = 1.6f;
    [Tooltip("Pulse depth as a fraction of the current alpha. 0.2 = ±20% modulation.")]
    [Range(0f, 1f)] public float pulseAmount = 0.25f;

    [Header("Texture")]
    [Tooltip("Resolution of the procedural gradient texture. 256 looks great and costs almost nothing.")]
    public int textureSize = 256;
    [Tooltip("Distance from center (0..1) where the gradient starts to fade in. Smaller = vignette eats more of the screen.")]
    [Range(0f, 1f)] public float innerRadius = 0.42f;
    [Tooltip("Distance from center (0..1) where the gradient is fully opaque. 1 = corners only.")]
    [Range(0f, 1.5f)] public float outerRadius = 1.05f;

    private Image image;
    private float currentDisplayAlpha;
    private float hitFlashAlpha; // current contribution from the most recent hit; decays to 0 over hitFlashFadeTime

    private void Awake()
    {
        Instance = this;
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Trigger a brief red vignette flash. Stacks additively on top of the
    /// low-HP vignette and decays back to zero over <see cref="hitFlashFadeTime"/>.
    /// Called by Hero.TakeDamage.
    /// </summary>
    public void Flash()
    {
        // Peak overrides any in-flight flash so successive hits don't dim the
        // signal — the brightest flash always wins, and the falloff resumes
        // from there.
        if (hitFlashPeakAlpha > hitFlashAlpha) hitFlashAlpha = hitFlashPeakAlpha;
    }

    private void BuildUI()
    {
        // Hide leftover scene children (defensive — for the same reason as
        // PowerUpChoiceUI).
        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(false);

        GameObject canvasGo = new GameObject("HealthVignetteCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // above HUD (100), below powerup panel (200)

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // no scaling — we want literal screen coverage

        // Disable raycasting so the vignette never blocks clicks on real UI.
        canvasGo.GetComponent<GraphicRaycaster>().enabled = false;

        GameObject imgGo = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)imgGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        image = imgGo.GetComponent<Image>();
        image.sprite = BuildVignetteSprite();
        image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);
        image.raycastTarget = false;
        image.preserveAspect = false; // stretch — we *want* the elliptical look on wide screens
    }

    private Sprite BuildVignetteSprite()
    {
        int size = Mathf.Max(16, textureSize);
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDist = center.magnitude;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist01 = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float a = Mathf.InverseLerp(innerRadius, outerRadius, dist01);
                a = Mathf.SmoothStep(0f, 1f, a); // ease for a softer falloff
                // Vertex color is multiplied with the texture, so we leave the
                // RGB as white — the Image.color tint paints it red later.
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f));
    }

    private void Update()
    {
        if (image == null) return;

        // Compute target alpha from hero state.
        float targetAlpha = 0f;
        Hero hero = Hero.Instance;
        if (hero != null && !hero.IsDead && criticalHealth > 0f)
        {
            float hp = hero.CurrentHP;
            if (hp < criticalHealth)
            {
                // 1 at HP=0, 0 at HP=criticalHealth.
                float severity = 1f - Mathf.Clamp01(hp / criticalHealth);
                targetAlpha = maxAlpha * severity;
            }
        }

        // Ease the displayed alpha toward the target so transitions are smooth.
        currentDisplayAlpha = Mathf.MoveTowards(
            currentDisplayAlpha, targetAlpha, fadeSpeed * Time.deltaTime);

        // Subtle pulse on top of the displayed alpha.
        float a = currentDisplayAlpha;
        if (pulseSpeed > 0f && pulseAmount > 0f && currentDisplayAlpha > 0.001f)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            a *= pulse;
        }

        // Hit flash: linear decay from peak to zero. Stacks additively on
        // top of the low-HP vignette so a hit at full HP still reads as a
        // clean red flash, and a hit during low HP intensifies what's
        // already there.
        if (hitFlashAlpha > 0f)
        {
            float decayPerSec = hitFlashFadeTime > 0.0001f ? hitFlashPeakAlpha / hitFlashFadeTime : float.MaxValue;
            hitFlashAlpha = Mathf.Max(0f, hitFlashAlpha - decayPerSec * Time.deltaTime);
            a += hitFlashAlpha;
        }

        Color c = vignetteColor;
        c.a = Mathf.Clamp01(a);
        image.color = c;
    }
}
