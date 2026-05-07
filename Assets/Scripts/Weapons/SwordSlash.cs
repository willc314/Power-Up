using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One sword swing. Goes on the root of the swing prefab.
/// The visual is the prefab itself, usually a sword model.
/// The script positions and rotates the visual along an arc in front of the owner,
/// then destroys it after the swing duration.
///
/// Damage behavior:
///   This uses a fan-shaped gameplay hit area in front of the Hero.
///   The sword visual can now be adjusted freely without breaking the hitbox.
///
/// Each enemy is hit at most once per swing.
/// </summary>
public class SwordSlash : MonoBehaviour
{
    [Header("Swing Visual")]
    [Tooltip("Total angle the sword visual sweeps through, in degrees.")]
    public float arcDegrees = 140f;

    [Tooltip("Time the swing takes to complete, in seconds.")]
    public float duration = 0.25f;

    [Tooltip("Distance from the owner's center to the sword visual arc.")]
    public float radius = 1.4f;

    [Tooltip("Vertical offset above the owner's pivot so the visual sits at chest/shoulder height.")]
    public float verticalOffset = 1.0f;

    [Header("Gameplay Hit Area")]
    [Tooltip("How wide the damaging fan is in front of the owner. Usually same as Arc Degrees.")]
    public float hitArcDegrees = 140f;

    [Tooltip("Minimum distance from the owner that can be hit. Keep low so enemies close to the Hero still get hit.")]
    public float hitInnerRadius = 0.3f;

    [Tooltip("Maximum distance from the owner that can be hit. This should cover the whole sword swing, not just the tip.")]
    public float hitOuterRadius = 2.6f;

    [Tooltip("Vertical center of the damage check above the owner.")]
    public float hitHeight = 1.0f;

    [Tooltip("Vertical thickness of the hit check. Larger values catch short/tall enemy colliders more reliably.")]
    public float hitVerticalTolerance = 1.5f;

    [Tooltip("Extra radius used by the broad physics check before angle filtering.")]
    public float broadCheckPadding = 0.4f;

    [Tooltip("Draws the fan hit area in the Scene view during play.")]
    public bool debugDrawHitbox = true;

    [Header("Legacy Blade Hitbox")]
    [Tooltip("Kept for old prefabs. No longer used for damage unless Use Legacy Blade Capsule is checked.")]
    public float bladeLength = 2.5f;

    [Tooltip("Kept for old prefabs. No longer used for damage unless Use Legacy Blade Capsule is checked.")]
    public float bladeRadius = 0.35f;

    [Tooltip("Kept for old prefabs. No longer used for damage unless Use Legacy Blade Capsule is checked.")]
    public BladeAxis bladeAxis = BladeAxis.Y;

    [Tooltip("Kept for old prefabs. No longer used for damage unless Use Legacy Blade Capsule is checked.")]
    public Vector3 bladeCenterOffset = new Vector3(0f, 1.25f, 0f);

    [Tooltip("If true, the hitbox is a capsule along the actual sword blade (it sweeps through enemies as the sword arcs). If false, uses the static fan-shaped area in front of the hero. Blade capsule feels more responsive and weighty.")]
    public bool useLegacyBladeCapsule = true;

    public enum BladeAxis { X = 0, Y = 1, Z = 2 }

    [Header("Visual Tweaks")]
    [Tooltip("X rotation on the model. 90 typically lays an upright sword flat.")]
    public float modelPitch = 90f;

    [Tooltip("Y rotation on the model. Change to 90 or -90 if your sword model points sideways.")]
    public float modelYaw = 90f;

    [Tooltip("Z rotation on the model. Use to flip the blade if needed.")]
    public float modelRoll = 0f;

    // ---- Zenith (set by SwordWeapon at spawn time when zenithApplied) ----
    [System.NonSerialized] public bool zenithMode = false;
    [System.NonSerialized] public float zenithSemiMinor = 1.4f;
    /// <summary>Major (forward / cursor) semi-axis of the zenith ellipse.
    /// SwordWeapon snapshots the cursor's actual distance from the hero at
    /// swing-spawn time and writes it here so the swing's apex lands
    /// exactly on the cursor instead of at a fixed orbit radius. 0 (default)
    /// falls back to the slash's <see cref="radius"/> field.</summary>
    [System.NonSerialized] public float zenithMajorRadius = 0f;
    [System.NonSerialized] public Color trailTint = Color.white;

    [Header("Zenith Trail Tuning")]
    [Tooltip("Multiplier applied to TrailRenderer.startWidth / endWidth in Zenith mode. 2 = trail is 2× the prefab-authored width while Zenith is active. Non-Zenith swings keep using the Tiny.Trail prefab visuals untouched.")]
    public float zenithTrailWidthMultiplier = 2.0f;
    [Tooltip("Alpha along the LENGTH at the trail HEAD (newest position, near the blade tip).")]
    [Range(0f, 1f)] public float zenithTrailHeadAlpha = 1.0f;
    [Tooltip("Alpha along the LENGTH at the trail TAIL (oldest position, fading into the air behind the blade).")]
    [Range(0f, 1f)] public float zenithTrailTailAlpha = 0.0f;
    [Tooltip("Alpha along the WIDTH on the side facing the BLADE TIP. Combines multiplicatively with the head/tail length fade by sampling a runtime-baked vertical gradient on the trail material's main texture.")]
    [Range(0f, 1f)] public float zenithTrailTipSideAlpha = 1.0f;
    [Tooltip("Alpha along the WIDTH on the side facing the SWORD BASE. Lower values fade the trail's bottom edge so the trail visually anchors to the blade tip.")]
    [Range(0f, 1f)] public float zenithTrailBaseSideAlpha = 0.0f;
    [Tooltip("Shader property name on the trail material used for the runtime width-fade gradient. '_MainTex' works with the standard Sprites/Particles unlit shaders. If your trail uses a custom shader sampling a different 2D texture for vertex color modulation, override here.")]
    public string zenithTrailWidthTextureProperty = "_MainTex";
    [Tooltip("If the visible width fade is reversed (tip-side ends up the transparent edge), flip this. Just inverts the bake direction without you having to swap the alpha values.")]
    public bool zenithTrailFlipWidthGradient = false;

    // Cached gradient textures and property block, shared across all swings.
    // We pre-bake TWO textures — one canonical (V=0 base, V=1 tip) for
    // left-to-right swings and one V-reversed for right-to-left swings —
    // and swap which one the property block points at based on swing
    // direction. We do NOT rely on _MainTex_ST to flip V via the property
    // block: many trail / particle / unlit shaders sample _MainTex
    // directly without applying TRANSFORM_TEX, so an _ST override silently
    // no-ops and the gradient stays mirrored on alternating swings. Two
    // pre-baked textures bypass that entirely. Memory cost is ~512 bytes
    // for both (1×64 RGBA32 each). Rebuilt only when the tip/base alpha
    // inspector values change.
    private static Texture2D zenithWidthGradientCanonical_;
    private static Texture2D zenithWidthGradientFlipped_;
    private static float cachedTipSideAlpha_  = float.NaN;
    private static float cachedBaseSideAlpha_ = float.NaN;
    private static MaterialPropertyBlock zenithMpb_;
    private static int   zenithWidthTexId_    = -1;
    private static string zenithWidthTexProp_ = null;

    private Transform owner;
    private float damage;
    /// <summary>
    /// Owner rotation snapshotted at <see cref="Init"/> time so the swing's
    /// arc stays in its initial world-facing direction even if the player
    /// rotates mid-swing (cursor turn / dash-induced facing change).
    /// </summary>
    private Quaternion frozenRotation;
    /// <summary>
    /// Owner position snapshotted at <see cref="Init"/>. Combined with
    /// frozenRotation this fully decouples the swing from the player —
    /// dashing, sprinting, or being knocked across the arena mid-swing
    /// leaves the slash committed to its original world location and
    /// arc, so swings always complete on the spot they started.
    /// </summary>
    private Vector3 frozenPosition;
    private LayerMask enemyLayers;
    private bool rightToLeft;
    private float timer;

    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[64];

    // Cached lookup for the Meteor general augment (see MeteorArmer). If the
    // swing was rolled at fire time, the first valid enemy hit consumes it
    // and spawns a meteor at the enemy's position. Lazily fetched because
    // the armer is attached AFTER Init runs by SwordWeapon.Fire.
    private MeteorArmer meteorArmer;

    public void Init(Transform owner, float damage, LayerMask enemyLayers, bool rightToLeft)
    {
        this.owner = owner;
        this.damage = damage;
        this.enemyLayers = enemyLayers;
        this.rightToLeft = rightToLeft;
        // Snapshot rotation AND position now — the swing is fully
        // committed to the player's transform at the click frame and is
        // unaffected by any subsequent movement or rotation. Mid-swing
        // dashes, knockbacks, or cursor turns leave the slash on its
        // original arc at its original world location.
        this.frozenRotation = owner != null ? owner.rotation : Quaternion.identity;
        this.frozenPosition = owner != null ? owner.position : transform.position;

        timer = 0f;

        // Trail mode dispatch:
        //
        // Non-Zenith swings keep the original Tiny.Trail (mesh-baked) look —
        // it's authored on the prefab and feels right for the regular sword.
        // We force-disable any Unity TrailRenderer children so the augment
        // trail doesn't bleed into the base swings.
        //
        // Zenith swings use the Unity TrailRenderer instead: it's cheap,
        // tolerates fast arcs, and supports a clean head→tail alpha fade
        // (head near the blade tip stays opaque, tail dissolves away).
        // Tiny.Trail children are turned off for Zenith so the two trail
        // systems don't double up. Width is multiplied by
        // zenithTrailWidthMultiplier so the prefab-authored proportions
        // stay intact while the augment reads as visibly bigger.
        if (zenithMode)
        {
            Color head = trailTint; head.a = Mathf.Clamp01(zenithTrailHeadAlpha);
            Color tail = trailTint; tail.a = Mathf.Clamp01(zenithTrailTailAlpha);

            // Bake / fetch the width-axis alpha gradient textures and prepare
            // the MaterialPropertyBlock once per swing. We don't mutate
            // tr.material (which would instance and leak materials per
            // swing) — instead we override _MainTex via a property block,
            // which composes multiplicatively with the trail's vertex color
            // (head/tail length fade) inside any standard texture-sampling
            // shader. If the trail material doesn't sample _MainTex, set
            // zenithTrailWidthTextureProperty to whatever sampler does.
            //
            // We bake TWO textures (canonical + V-reversed) and swap based
            // on swing direction: when the sword model is yaw-flipped on
            // rightToLeft swings the trail's V axis flips with it, so the
            // gradient would visibly reverse on alternating swings without
            // a per-swing correction. Pre-baked textures (instead of an
            // _ST UV-flip) guarantee the correction takes effect regardless
            // of how the trail's shader handles UV transforms — many
            // particle/unlit shaders ignore _MainTex_ST overrides from a
            // property block, which is why the previous _ST-based flip
            // didn't visibly do anything on this material.
            EnsureZenithWidthGradients(
                Mathf.Clamp01(zenithTrailTipSideAlpha),
                Mathf.Clamp01(zenithTrailBaseSideAlpha));
            int widthTexId = GetZenithWidthTexId(zenithTrailWidthTextureProperty);
            if (zenithMpb_ == null) zenithMpb_ = new MaterialPropertyBlock();

            // Compose the V-axis flip:
            //   - rightToLeft: yaw-mirrored swing flips the visible V edge.
            //   - inspector toggle: lets the user correct an inverted setup
            //     without having to swap the alpha values.
            // XOR so two flips cancel, matching natural inversion semantics.
            bool useFlipped = rightToLeft ^ zenithTrailFlipWidthGradient;
            Texture2D widthTex = useFlipped ? zenithWidthGradientFlipped_ : zenithWidthGradientCanonical_;

            foreach (var tr in GetComponentsInChildren<TrailRenderer>(true))
            {
                tr.enabled    = true;
                tr.startColor = head;
                tr.endColor   = tail;
                // Read whatever the prefab authored as the "1× width" baseline
                // and scale it. We touch widthMultiplier (rather than
                // startWidth/endWidth) so designers who set a non-trivial
                // widthCurve keep their authored profile and just get it
                // scaled by the multiplier.
                float mul = Mathf.Max(0.01f, zenithTrailWidthMultiplier);
                tr.widthMultiplier = mul;

                // Apply the width-axis gradient via property block. Read
                // existing block first so we don't clobber other property
                // overrides on the renderer.
                tr.GetPropertyBlock(zenithMpb_);
                zenithMpb_.SetTexture(widthTexId, widthTex);
                tr.SetPropertyBlock(zenithMpb_);
            }
            foreach (var t in GetComponentsInChildren<Tiny.Trail>(true))
            {
                t.RuntimeTintColor = trailTint;
                t.enabled = false;
            }
        }
        else
        {
            // Suppress any TrailRenderer left on the prefab so the regular
            // sword swing keeps using the mini Tiny.Trail look.
            foreach (var tr in GetComponentsInChildren<TrailRenderer>(true))
                tr.enabled = false;
            foreach (var t in GetComponentsInChildren<Tiny.Trail>(true))
                t.enabled = true;
        }

        // Snap the visual to the arc-start pose BEFORE the first render.
        // SwordWeapon.Fire() instantiates the prefab at the hero's spawn
        // position with the hero's rotation — without this the sword would
        // render for one frame at "in front of hero, hero-facing-direction"
        // (no modelPitch/modelYaw applied) before Update()'s first call to
        // UpdateVisual() snaps it onto the arc the following frame. That
        // produced a visible 1-frame pop at the start of every swing.
        UpdateVisual(0f);

        // First hit check on the click frame so the sword feels snappy.
        // Without this there's a ~1-frame gap before the first damage tick runs in Update().
        if (useLegacyBladeCapsule)
            DamageUsingLegacyBladeCapsule();
        else
            DamageUsingFanArea();

        Destroy(gameObject, duration + 0.1f);
    }

    private void Update()
    {
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        UpdateVisual(t);

        if (useLegacyBladeCapsule)
            DamageUsingLegacyBladeCapsule();
        else
            DamageUsingFanArea();

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void UpdateVisual(float t)
    {
        // Ease-out: fast start, slow finish. Same curve for Zenith and
        // non-Zenith swings.
        float eased = 1f - Mathf.Pow(1f - t, 2f);

        // Zenith path: full 360° elliptical sweep. The sword starts BEHIND
        // the player (-180° from forward), passes through the cursor
        // direction (0°), and ends behind again (+180°). The path is an
        // ellipse with its major axis aligned along owner.forward (semi-axis
        // = radius, the longer cursor-reach) and minor axis along owner.right
        // (semi-axis = zenithSemiMinor, the narrower side reach). This makes
        // the sword feel like it stretches forward toward the cursor and is
        // closer to the body when sweeping past the sides.
        // Frozen-rotation derived basis vectors — the arc's facing is
        // committed to the click-frame direction.
        Vector3 frozenForward = frozenRotation * Vector3.forward;
        Vector3 frozenRight   = frozenRotation * Vector3.right;

        // Anchor interpolates from the FROZEN spawn position at swing
        // start to the LIVE player position at swing end. This way the
        // start of the arc reads as locked to where the player clicked
        // (apex still lands on the original cursor target), but the
        // sword always finishes its sweep behind the player's CURRENT
        // position even if they walked / dashed during the swing.
        Vector3 livePos = owner != null ? owner.position : frozenPosition;
        Vector3 anchor  = Vector3.Lerp(frozenPosition, livePos, eased);

        if (zenithMode)
        {
            // Always sweep the full ±180° regardless of arcDegrees. Direction
            // alternation (rightToLeft) flips the sign so consecutive swings
            // come from opposite sides like the regular swing.
            float startAngle = rightToLeft ?  180f : -180f;
            float endAngle   = rightToLeft ? -180f :  180f;
            float angle = Mathf.Lerp(startAngle, endAngle, eased);
            float angleRad = angle * Mathf.Deg2Rad;

            // Asymmetric ellipse — front apex on the cursor, back apex at
            // the sword's normal orbit radius BEHIND the player. We achieve
            // this by offsetting the ellipse's center forward of the player
            // by half the difference, so:
            //   front end  = +centerForward + semiMajor = cursorDistance
            //   back end   = +centerForward - semiMajor = -radius
            // (radius here is the normal sword orbit, i.e. the start/end
            // distance the player is used to from non-Zenith swings.)
            float frontReach = zenithMajorRadius > 0.001f ? zenithMajorRadius : radius;
            float backReach  = radius;
            float semiMajor    = (frontReach + backReach) * 0.5f;
            float centerForward = (frontReach - backReach) * 0.5f;

            // Parametric ellipse: forward = centerForward + semiMajor * cos(θ),
            //                     side    = zenithSemiMinor * sin(θ).
            // θ=0    → sword apex at cursor (forward = +frontReach).
            // θ=±90  → sword at ±zenithSemiMinor on the side, slightly
            //           forward of the player (centerForward offset).
            // θ=±180 → sword at the normal start/return point behind the
            //           player (forward = -backReach = -radius).
            float forward = centerForward + semiMajor * Mathf.Cos(angleRad);
            float side    = zenithSemiMinor * Mathf.Sin(angleRad);

            Vector3 dir = frozenForward * forward + frozenRight * side;
            transform.position = anchor + dir + Vector3.up * verticalOffset;

            // Outward-yaw is derived from the ELLIPSE CENTER, not the player.
            // The asymmetric ellipse's center sits forward of the player by
            // centerForward, so a rotation based on (sword - player) would
            // skew the sword's facing — particularly at the sides and back.
            // Vector from center to sword position equals the parametric
            // tangent-perpendicular, which is the natural outward direction:
            //   outward = forward.center * (semiMajor cosθ) + right * (semiMinor sinθ)
            Vector3 outwardFromCenter = frozenForward * (semiMajor * Mathf.Cos(angleRad))
                                       + frozenRight   * (zenithSemiMinor * Mathf.Sin(angleRad));
            Quaternion radial = Quaternion.LookRotation(
                outwardFromCenter.sqrMagnitude > 0.0001f ? outwardFromCenter.normalized : frozenForward,
                Vector3.up);
            float effectiveYaw = rightToLeft ? -modelYaw : modelYaw;
            transform.rotation = radial * Quaternion.Euler(modelPitch, effectiveYaw, modelRoll);
            return;
        }

        // Default circular path (pre-Zenith / non-Zenith swords). Uses the
        // frozen rotation so the swing's arc stays in its initial world
        // direction even if the player rotates mid-swing.
        float startAngleC = -arcDegrees * 0.5f;
        float endAngleC = arcDegrees * 0.5f;

        if (rightToLeft)
        {
            float tmp = startAngleC;
            startAngleC = endAngleC;
            endAngleC = tmp;
        }

        float angleC = Mathf.Lerp(startAngleC, endAngleC, eased);

        Quaternion radialC = frozenRotation * Quaternion.Euler(0f, angleC, 0f);
        Vector3 dirC = radialC * Vector3.forward;

        transform.position = anchor + dirC * radius + Vector3.up * verticalOffset;

        float effectiveYawC = rightToLeft ? -modelYaw : modelYaw;
        transform.rotation = radialC * Quaternion.Euler(modelPitch, effectiveYawC, modelRoll);
    }

    private void DamageUsingFanArea()
    {
        Vector3 center = owner.position + Vector3.up * hitHeight;
        float broadRadius = hitOuterRadius + broadCheckPadding;

        int n = Physics.OverlapSphereNonAlloc(
            center,
            broadRadius,
            hitBuffer,
            enemyLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < n; i++)
        {
            Collider col = hitBuffer[i];
            if (col == null)
                continue;

            Enemy enemy = col.GetComponentInParent<Enemy>();
            if (enemy == null || alreadyHit.Contains(enemy))
                continue;

            Vector3 closest = col.ClosestPoint(center);
            Vector3 flatOffset = closest - owner.position;
            flatOffset.y = 0f;

            float flatDistance = flatOffset.magnitude;

            if (flatDistance < hitInnerRadius || flatDistance > hitOuterRadius)
                continue;

            float verticalDifference = Mathf.Abs(closest.y - center.y);

            if (verticalDifference > hitVerticalTolerance)
                continue;

            Vector3 flatDirection = flatOffset.normalized;
            float angleFromForward = Vector3.Angle(owner.forward, flatDirection);

            if (angleFromForward > hitArcDegrees * 0.5f)
                continue;

            alreadyHit.Add(enemy);
            enemy.TakeDamage(damage);
            if (meteorArmer == null) meteorArmer = GetComponent<MeteorArmer>();
            if (meteorArmer != null) meteorArmer.TryConsume(enemy.transform.position);
        }

        if (debugDrawHitbox)
            DrawFanDebug();
    }

    private void DamageUsingLegacyBladeCapsule()
    {
        ComputeCapsuleEndpoints(out Vector3 p1, out Vector3 p2);

        int n = Physics.OverlapCapsuleNonAlloc(
            p1,
            p2,
            bladeRadius,
            hitBuffer,
            enemyLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < n; i++)
        {
            Enemy enemy = hitBuffer[i].GetComponentInParent<Enemy>();

            if (enemy != null && alreadyHit.Add(enemy))
            {
                enemy.TakeDamage(damage);
                if (meteorArmer == null) meteorArmer = GetComponent<MeteorArmer>();
                if (meteorArmer != null) meteorArmer.TryConsume(enemy.transform.position);
            }
        }

        if (debugDrawHitbox)
            DrawLegacyCapsuleDebug(p1, p2);
    }

    private void DrawLegacyCapsuleDebug(Vector3 p1, Vector3 p2)
    {
        // Center axis line.
        Debug.DrawLine(p1, p2, Color.red);

        // Approximate the two end-spheres as 8-segment rings perpendicular to the axis,
        // plus 4 parallel "tube" lines connecting them. Cheap to draw and clearly shows
        // the blade-shaped capsule sweeping with the sword.
        Vector3 axis = (p2 - p1);
        if (axis.sqrMagnitude < 0.0001f) return;
        axis.Normalize();

        Vector3 perp1 = Vector3.Cross(axis, Vector3.up);
        if (perp1.sqrMagnitude < 0.0001f) perp1 = Vector3.Cross(axis, Vector3.right);
        perp1.Normalize();
        Vector3 perp2 = Vector3.Cross(axis, perp1).normalized;

        const int segments = 8;
        Vector3 prevA = p1 + perp1 * bladeRadius;
        Vector3 prevB = p2 + perp1 * bladeRadius;
        for (int i = 1; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 offset = (Mathf.Cos(a) * perp1 + Mathf.Sin(a) * perp2) * bladeRadius;
            Vector3 nextA = p1 + offset;
            Vector3 nextB = p2 + offset;
            Debug.DrawLine(prevA, nextA, Color.red);
            Debug.DrawLine(prevB, nextB, Color.red);
            prevA = nextA;
            prevB = nextB;
        }

        // Four "tube" lines along the length so the capsule reads as a 3D shape.
        Debug.DrawLine(p1 + perp1 * bladeRadius, p2 + perp1 * bladeRadius, Color.red);
        Debug.DrawLine(p1 - perp1 * bladeRadius, p2 - perp1 * bladeRadius, Color.red);
        Debug.DrawLine(p1 + perp2 * bladeRadius, p2 + perp2 * bladeRadius, Color.red);
        Debug.DrawLine(p1 - perp2 * bladeRadius, p2 - perp2 * bladeRadius, Color.red);
    }

    private void ComputeCapsuleEndpoints(out Vector3 p1, out Vector3 p2)
    {
        Vector3 axisLocal = AxisVector(bladeAxis);
        Vector3 axisWorld = transform.TransformDirection(axisLocal);
        Vector3 center = transform.TransformPoint(bladeCenterOffset);

        float halfLen = Mathf.Max(0f, bladeLength * 0.5f - bladeRadius);

        p1 = center - axisWorld * halfLen;
        p2 = center + axisWorld * halfLen;
    }

    private static Vector3 AxisVector(BladeAxis axis)
    {
        switch (axis)
        {
            case BladeAxis.X:
                return Vector3.right;

            case BladeAxis.Y:
                return Vector3.up;

            case BladeAxis.Z:
                return Vector3.forward;
        }

        return Vector3.up;
    }

    private void DrawFanDebug()
    {
        Vector3 origin = owner.position + Vector3.up * hitHeight;

        Vector3 leftDir = Quaternion.Euler(0f, -hitArcDegrees * 0.5f, 0f) * owner.forward;
        Vector3 rightDir = Quaternion.Euler(0f, hitArcDegrees * 0.5f, 0f) * owner.forward;

        Debug.DrawLine(origin, origin + leftDir * hitOuterRadius, Color.red);
        Debug.DrawLine(origin, origin + rightDir * hitOuterRadius, Color.red);
        Debug.DrawLine(origin + owner.forward * hitInnerRadius, origin + owner.forward * hitOuterRadius, Color.yellow);
    }

    private void OnDrawGizmosSelected()
    {
        if (owner == null)
            return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(owner.position + Vector3.up * hitHeight, hitOuterRadius);

        Gizmos.color = new Color(1f, 1f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(owner.position + Vector3.up * hitHeight, hitInnerRadius);
    }

    /// <summary>
    /// Build (or refresh from cache) two 1×N alpha gradient textures used to
    /// fade the Zenith trail across its WIDTH axis. Both have white RGB; the
    /// alpha channel ramps either canonically (V=0 base → V=1 tip) or
    /// V-reversed (V=0 tip → V=1 base). The reversed bake is what
    /// right-to-left swings sample so the fade lands on the same blade
    /// edge regardless of swing direction.
    ///
    /// Sampled multiplicatively against the trail's vertex color (which
    /// already carries the head/tail length fade) so the two fades compose
    /// on screen. Cached statically — only rebuilt when the inspector
    /// alphas change. Textures are HideFlags.HideAndDontSave so they
    /// survive scene loads.
    /// </summary>
    private static void EnsureZenithWidthGradients(float tipSideAlpha, float baseSideAlpha)
    {
        if (zenithWidthGradientCanonical_ != null
            && zenithWidthGradientFlipped_   != null
            && Mathf.Approximately(cachedTipSideAlpha_,  tipSideAlpha)
            && Mathf.Approximately(cachedBaseSideAlpha_, baseSideAlpha))
        {
            return;
        }

        // Discard any prior bakes if the alphas changed so we don't leak.
        if (zenithWidthGradientCanonical_ != null) DestroyImmediate(zenithWidthGradientCanonical_);
        if (zenithWidthGradientFlipped_   != null) DestroyImmediate(zenithWidthGradientFlipped_);

        zenithWidthGradientCanonical_ = BakeAlphaGradient(baseSideAlpha, tipSideAlpha);  // V=0 base, V=1 tip
        zenithWidthGradientFlipped_   = BakeAlphaGradient(tipSideAlpha,  baseSideAlpha); // V=0 tip,  V=1 base
        cachedTipSideAlpha_  = tipSideAlpha;
        cachedBaseSideAlpha_ = baseSideAlpha;
    }

    /// <summary>
    /// Bake a 1×N RGBA32 texture with white RGB and an alpha gradient from
    /// <paramref name="alphaAtV0"/> at V=0 to <paramref name="alphaAtV1"/>
    /// at V=1. Used by the Zenith trail to apply a width-axis fade on top
    /// of the trail's existing length-axis vertex-color fade.
    /// </summary>
    private static Texture2D BakeAlphaGradient(float alphaAtV0, float alphaAtV1)
    {
        const int H = 64;
        var tex = new Texture2D(1, H, TextureFormat.RGBA32, false, true);
        tex.wrapMode   = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags  = HideFlags.HideAndDontSave;

        var pixels = new Color[H];
        for (int i = 0; i < H; i++)
        {
            float v = (H == 1) ? 1f : i / (float)(H - 1);
            float a = Mathf.Lerp(alphaAtV0, alphaAtV1, v);
            pixels[i] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    /// <summary>
    /// Resolve and cache the shader property ID for the trail's width-fade
    /// texture sampler. Most shaders use "_MainTex"; the inspector field
    /// lets prefabs that use a different sampler retarget without a code
    /// change.
    /// </summary>
    private static int GetZenithWidthTexId(string propName)
    {
        if (string.IsNullOrEmpty(propName)) propName = "_MainTex";
        if (zenithWidthTexProp_ != propName)
        {
            zenithWidthTexId_   = Shader.PropertyToID(propName);
            zenithWidthTexProp_ = propName;
        }
        return zenithWidthTexId_;
    }
}