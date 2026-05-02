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

    [Tooltip("If true, uses the old blade capsule hitbox instead of the new full-swing fan hitbox.")]
    public bool useLegacyBladeCapsule = false;

    public enum BladeAxis { X = 0, Y = 1, Z = 2 }

    [Header("Visual Tweaks")]
    [Tooltip("X rotation on the model. 90 typically lays an upright sword flat.")]
    public float modelPitch = 90f;

    [Tooltip("Y rotation on the model. Change to 90 or -90 if your sword model points sideways.")]
    public float modelYaw = 90f;

    [Tooltip("Z rotation on the model. Use to flip the blade if needed.")]
    public float modelRoll = 0f;

    private Transform owner;
    private float damage;
    private LayerMask enemyLayers;
    private bool rightToLeft;
    private float timer;

    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[64];

    public void Init(Transform owner, float damage, LayerMask enemyLayers, bool rightToLeft)
    {
        this.owner = owner;
        this.damage = damage;
        this.enemyLayers = enemyLayers;
        this.rightToLeft = rightToLeft;

        timer = 0f;

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
        float eased = 1f - Mathf.Pow(1f - t, 2f);

        float startAngle = -arcDegrees * 0.5f;
        float endAngle = arcDegrees * 0.5f;

        if (rightToLeft)
        {
            float tmp = startAngle;
            startAngle = endAngle;
            endAngle = tmp;
        }

        float angle = Mathf.Lerp(startAngle, endAngle, eased);

        Quaternion radial = owner.rotation * Quaternion.Euler(0f, angle, 0f);
        Vector3 dir = radial * Vector3.forward;

        transform.position = owner.position + dir * radius + Vector3.up * verticalOffset;

        float effectiveYaw = rightToLeft ? -modelYaw : modelYaw;
        transform.rotation = radial * Quaternion.Euler(modelPitch, effectiveYaw, modelRoll);
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
                enemy.TakeDamage(damage);
        }

        if (debugDrawHitbox)
            Debug.DrawLine(p1, p2, Color.red);
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
}