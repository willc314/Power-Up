using UnityEngine;

/// <summary>
/// Plays animation clips on an Enemy based on its state. Drop this on each
/// enemy prefab alongside an Animator (and the pack's Animator Controller),
/// then set the clip names in the Inspector to match what that pack used.
///
/// Why clip names instead of triggers/parameters: the Animator Controllers that
/// ship with each pack (Slime.controller, Cactus.controller, MushroomAngry,
/// etc.) don't have transitions or parameters set up — they're demo
/// controllers full of states. CrossFadeInFixedTime(stateName) lets us play
/// any state directly without setting up transitions, which means we don't
/// have to author a controller per enemy.
///
/// Per-pack clip names:
///   Slime / TurtleShell:  IdleNormal, WalkFWD, Attack01, GetHit, Die
///   Cactus:               Cactus_IdleNormal, Cactus_WalkFWD, Cactus_Attack01,
///                         Cactus_GetHit, Cactus_Die
///   MushroomAngry:        Mushroom_IdleNormalAngry, Mushroom_walkFWDAngry,
///                         Mushroom_Attack01Angry, Mushroom_GetHitAngry,
///                         Mushroom_DieAngry
/// </summary>
public class EnemyAnimator : MonoBehaviour
{
    [Header("Animator (auto-found if empty)")]
    [Tooltip("Animator component. Auto-found in this GameObject or children if left empty.")]
    public Animator animator;

    [Header("Clip Names")]
    [Tooltip("Looping idle clip played when the enemy isn't moving.")]
    public string idleClip = "IdleNormal";
    [Tooltip("Looping walk/run clip played when the enemy is moving.")]
    public string walkClip = "WalkFWD";
    [Tooltip("Attack clip played once when the enemy fires/melees.")]
    public string attackClip = "Attack01";
    [Tooltip("Hit reaction clip played once when the enemy takes damage. Leave empty to skip.")]
    public string hitClip = "GetHit";
    [Tooltip("Death clip played once when the enemy dies. Animation finishes before the GameObject is destroyed.")]
    public string dieClip = "Die";
    [Tooltip("Victory clip played (and looped) on every alive enemy when the Hero dies. Leave empty to skip.")]
    public string victoryClip = "Victory";

    [Header("Tuning")]
    [Tooltip("Velocity (units/sec) above which the enemy is considered moving.")]
    public float walkVelocityThreshold = 0.1f;
    [Tooltip("Seconds for state crossfades.")]
    public float crossfadeDuration = 0.15f;

    private Rigidbody rb;
    private string currentLocomotionState = "";
    private bool isDead;
    private bool celebrating;
    private float oneShotEndTime;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody>();
    }

    private void OnEnable()  { Hero.OnHeroDied += HandleHeroDied; }
    private void OnDisable() { Hero.OnHeroDied -= HandleHeroDied; }

    private void HandleHeroDied()
    {
        if (isDead || celebrating || animator == null) return;
        if (string.IsNullOrEmpty(victoryClip) || !HasState(victoryClip)) return;
        celebrating = true;
        animator.CrossFadeInFixedTime(victoryClip, crossfadeDuration);
        currentLocomotionState = "";
    }

    private void Update()
    {
        if (isDead || celebrating || animator == null) return;
        // Don't override an attack/hit one-shot until it's done.
        if (Time.time < oneShotEndTime) return;

        Vector3 v = rb != null ? rb.velocity : Vector3.zero;
        v.y = 0f;
        bool moving = v.sqrMagnitude > walkVelocityThreshold * walkVelocityThreshold;

        string desired = moving ? walkClip : idleClip;
        if (string.IsNullOrEmpty(desired)) return;
        if (currentLocomotionState == desired) return;
        if (!HasState(desired)) return;

        animator.CrossFadeInFixedTime(desired, crossfadeDuration);
        currentLocomotionState = desired;
    }

    public void OnAttack()    => PlayOneShot(attackClip);
    public void OnHit()       => PlayOneShot(hitClip);
    public void OnDie()
    {
        isDead = true;
        if (animator == null) return;
        if (string.IsNullOrEmpty(dieClip) || !HasState(dieClip)) return;
        animator.CrossFadeInFixedTime(dieClip, 0.05f);
        currentLocomotionState = "";
    }

    private void PlayOneShot(string clip)
    {
        if (animator == null || string.IsNullOrEmpty(clip) || !HasState(clip)) return;
        animator.CrossFadeInFixedTime(clip, 0.05f);
        // Block locomotion override until the clip's nominal length passes.
        AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(0);
        // The state we just crossfaded to isn't current yet, so use a small constant
        // duration as the lockout window. 0.4s covers most attack/hit clips.
        oneShotEndTime = Time.time + 0.4f;
        currentLocomotionState = ""; // force re-evaluation after the one-shot
    }

    private bool HasState(string clip)
    {
        return animator.HasState(0, Animator.StringToHash(clip));
    }
}
