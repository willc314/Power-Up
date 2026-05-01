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

    [Header("Init")]
    [Tooltip("If true, the camera snaps to the correct position on the first frame instead of easing in from wherever it started.")]
    public bool snapOnStart = true;

    private Vector3 currentVelocity; // used by SmoothDamp

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

        Vector3 desired = target.position + offset;

        if (smoothTime <= 0f)
        {
            transform.position = desired;
        }
        else
        {
            float maxSpd = maxSpeed > 0f ? maxSpeed : Mathf.Infinity;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref currentVelocity, smoothTime, maxSpd, Time.deltaTime);
        }

        if (lookAtTarget) transform.LookAt(target);
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
