using UnityEngine;

/// <summary>
/// Smoothly follows a target (typically the Hero) from a fixed offset.
/// Designed for top-down or angled top-down cameras.
///
/// Setup:
///   1. Add this script to your Main Camera.
///   2. Either drag the Hero into the Target field in the Inspector,
///      OR leave it empty and ArenaGenerator will assign it after spawning the hero,
///      OR enable "Auto Find Hero" so the script grabs Hero.Instance on its own.
///   3. Tune Offset and Smooth Time to taste.
///
/// Recommended offsets:
///   * Pure top-down:        Offset (0, 30, 0)   + Camera rotation (90, 0, 0)
///   * Angled top-down:      Offset (0, 25, -12) + Camera rotation (60, 0, 0)
///   * Over-the-shoulder-ish:Offset (0, 15, -10) + Camera rotation (50, 0, 0)
/// </summary>
[DefaultExecutionOrder(100)] // run after Hero/ArenaGenerator so the target is set
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("What the camera follows. If empty and 'Auto Find Hero' is on, the script will grab Hero.Instance automatically.")]
    public Transform target;
    [Tooltip("If true and Target is null, the camera will look up Hero.Instance every LateUpdate until it finds one.")]
    public bool autoFindHero = true;

    [Header("Offset")]
    [Tooltip("World-space offset from the target. (0,30,0) = directly above. (0,25,-12) = slightly behind for an angled view.")]
    public Vector3 offset = new Vector3(0f, 30f, 0f);

    [Header("Smoothing")]
    [Tooltip("Approximate time (seconds) the camera takes to catch up to the target. 0 = instant snap, 0.1-0.3 feels good for action games, 0.5+ feels floaty.")]
    [Range(0f, 2f)] public float smoothTime = 0.15f;
    [Tooltip("Maximum speed the camera can move. -1 = unlimited.")]
    public float maxSpeed = -1f;

    [Header("Rotation")]
    [Tooltip("If true, the camera continuously points at the target. If false, the camera keeps its Inspector rotation (best for true top-down).")]
    public bool lookAtTarget = false;

    [Header("Cursor Lean")]
    [Tooltip("How strongly the camera leans toward the mouse cursor. 0 = ignore cursor. 0.2-0.4 feels subtle and helpful.")]
    [Range(0f, 1f)] public float cursorLookAhead = 0.25f;
    [Tooltip("Maximum world-units the camera can lean toward the cursor, no matter how far the cursor is.")]
    public float maxLookAheadDistance = 6f;
    [Tooltip("Y-height of the imaginary ground plane the cursor is projected onto. Match the hero's feet/ground height (usually 0).")]
    public float aimPlaneY = 0f;

    [Header("Init")]
    [Tooltip("If true, the camera snaps to the correct position on the first frame instead of easing in from wherever it started.")]
    public bool snapOnStart = true;

    [Header("Death Cam")]
    [Tooltip("When EnterDeathCam is called, the offset is multiplied by this factor — smaller = more zoomed in. 0.55 reads as a clear close-up without going claustrophobic.")]
    [Range(0.1f, 1f)] public float deathZoomFactor = 0.55f;
    [Tooltip("Seconds the camera takes to ease from its normal offset down to the zoomed offset.")]
    public float deathTransitionTime = 0.6f;
    [Tooltip("Smoothing time used while in death cam (overrides smoothTime). Lower = the corpse stays centered more tightly.")]
    public float deathSmoothTime = 0.25f;

    private Vector3 currentVelocity; // used by SmoothDamp
    private Camera cam;

    // Screen shake state (driven by Shake()).
    private float shakeAmplitude;
    private float shakeTimer;
    private float shakeDuration;

    // Death cam state.
    private bool deathCam;
    private Vector3 deathInitialOffset;
    private float deathLerpT;

    public bool IsInDeathCam => deathCam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        if (target == null && autoFindHero && Hero.Instance != null)
            target = Hero.Instance.transform;

        if (snapOnStart && target != null)
            transform.position = target.position + offset;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            if (autoFindHero && Hero.Instance != null) target = Hero.Instance.transform;
            else return;
        }

        // Death cam takes over: zoom-in lerp, no cursor lean, no shake.
        if (deathCam)
        {
            UpdateDeathCam();
            if (lookAtTarget) transform.LookAt(target);
            return;
        }

        Vector3 desired = target.position + offset + GetCursorLeanOffset();

        if (smoothTime <= 0f)
        {
            transform.position = desired;
        }
        else
        {
            float maxSpd = maxSpeed > 0f ? maxSpeed : Mathf.Infinity;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref currentVelocity, smoothTime, maxSpd, Time.deltaTime);
        }

        // Apply shake AFTER smoothing — otherwise SmoothDamp averages out the high-frequency noise and the shake is invisible.
        transform.position += GetShakeOffset();

        if (lookAtTarget) transform.LookAt(target);
    }

    /// <summary>
    /// Switch into a focused death cam: zoom in on <paramref name="corpse"/>,
    /// stop tracking the cursor, and cancel any in-progress screen shake.
    /// Called by Hero.Die so the player gets a clean focus on the dying hero.
    /// </summary>
    public void EnterDeathCam(Transform corpse)
    {
        if (corpse != null) target = corpse;
        if (deathCam) return;

        deathCam = true;
        deathInitialOffset = offset;
        deathLerpT = 0f;

        // Cancel any pending screen shake so the focus reads cleanly.
        shakeTimer = 0f;
        shakeAmplitude = 0f;
    }

    /// <summary>Drop back into the normal follow behavior. Useful for restart flows.</summary>
    public void ExitDeathCam()
    {
        deathCam = false;
        deathLerpT = 0f;
    }

    private void UpdateDeathCam()
    {
        // Lerp 0 → 1 over deathTransitionTime, then hold.
        if (deathTransitionTime > 0f)
            deathLerpT = Mathf.Min(1f, deathLerpT + Time.deltaTime / deathTransitionTime);
        else
            deathLerpT = 1f;

        Vector3 zoomedOffset = Vector3.Lerp(deathInitialOffset, deathInitialOffset * deathZoomFactor, deathLerpT);
        Vector3 desired = target.position + zoomedOffset;

        float smooth = deathSmoothTime > 0f ? deathSmoothTime : smoothTime;
        if (smooth <= 0f)
        {
            transform.position = desired;
        }
        else
        {
            float maxSpd = maxSpeed > 0f ? maxSpeed : Mathf.Infinity;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref currentVelocity, smooth, maxSpd, Time.deltaTime);
        }
    }

    /// <summary>
    /// Computes a small XZ offset that pulls the camera toward the mouse cursor.
    /// Returns Vector3.zero if the lean is disabled or the cursor can't be projected.
    /// </summary>
    private Vector3 GetCursorLeanOffset()
    {
        if (cursorLookAhead <= 0f || cam == null || target == null) return Vector3.zero;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, aimPlaneY, 0f));
        if (!ground.Raycast(ray, out float dist)) return Vector3.zero;

        Vector3 cursorWorld = ray.GetPoint(dist);
        Vector3 lean = cursorWorld - target.position;
        lean.y = 0f;
        lean *= cursorLookAhead;

        if (lean.magnitude > maxLookAheadDistance)
            lean = lean.normalized * maxLookAheadDistance;

        return lean;
    }

    /// <summary>
    /// Trigger a screen shake. Shakes are additive — re-calling extends or strengthens.
    /// </summary>
    public void Shake(float amplitude, float duration)
    {
        // Take the strongest shake currently active.
        if (amplitude > shakeAmplitude) shakeAmplitude = amplitude;
        if (duration > shakeTimer) { shakeTimer = duration; shakeDuration = duration; }
    }

    private Vector3 GetShakeOffset()
    {
        if (shakeTimer <= 0f) return Vector3.zero;
        shakeTimer -= Time.deltaTime;
        // Fall off linearly so the shake settles smoothly.
        float falloff = shakeDuration > 0f ? Mathf.Clamp01(shakeTimer / shakeDuration) : 0f;
        Vector3 noise = new Vector3(
            (Random.value - 0.5f) * 2f,
            0f,
            (Random.value - 0.5f) * 2f);
        if (shakeTimer <= 0f) { shakeAmplitude = 0f; }
        return noise * shakeAmplitude * falloff;
    }

    /// <summary>
    /// Called by ArenaGenerator (or anything else) to hand the camera a new target.
    /// Snaps the camera into place if Snap On Start is enabled.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (snapOnStart && target != null)
        {
            transform.position = target.position + offset;
            currentVelocity = Vector3.zero;
        }
    }
}
