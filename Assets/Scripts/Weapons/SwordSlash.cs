using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One sword swing. Goes on the root of the swing prefab.
/// The visual is the prefab itself (a sword model) — this script positions and
/// rotates it along an arc in front of the owner over `duration` seconds, then destroys it.
/// Each enemy is hit at most once per swing.
/// </summary>
public class SwordSlash : MonoBehaviour
{
    [Header("Swing")]
    [Tooltip("Total angle the sword sweeps through, in degrees.")]
    public float arcDegrees = 140f;
    [Tooltip("Time the swing takes to complete, in seconds.")]
    public float duration = 0.25f;
    [Tooltip("Distance from the owner's center to the swing arc.")]
    public float radius = 1.4f;
    [Tooltip("Vertical offset above the owner's pivot so the swing sits at chest/shoulder height.")]
    public float verticalOffset = 1.0f;

    [Header("Hit Detection")]
    [Tooltip("Radius of the hit-test sphere that travels along the blade each frame.")]
    public float hitRadius = 0.7f;

    [Header("Visual Tweaks")]
    [Tooltip("X rotation on the model. 90 typically lays an upright sword flat (parallel to ground).")]
    public float modelPitch = 90f;
    [Tooltip("Y rotation on the model. 90/-90 aims the blade along the swing tangent. The sign flips automatically for opposite-direction swings.")]
    public float modelYaw = 90f;
    [Tooltip("Z rotation on the model. Use to flip the blade if it's upside down after pitching.")]
    public float modelRoll = 0f;

    private Transform owner;
    private float damage;
    private LayerMask enemyLayers;
    private bool rightToLeft;
    private float timer;

    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[16];

    public void Init(Transform owner, float damage, LayerMask enemyLayers, bool rightToLeft)
    {
        this.owner = owner;
        this.damage = damage;
        this.enemyLayers = enemyLayers;
        this.rightToLeft = rightToLeft;
        timer = 0f;
        Destroy(gameObject, duration + 0.1f); // safety net
    }

    private void Update()
    {
        if (owner == null) { Destroy(gameObject); return; }

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        // Ease-out: fast at the start of the swing, decelerating into the follow-through.
        float eased = 1f - Mathf.Pow(1f - t, 2f);

        float startAngle = -arcDegrees * 0.5f;
        float endAngle   =  arcDegrees * 0.5f;
        if (rightToLeft) { float tmp = startAngle; startAngle = endAngle; endAngle = tmp; }

        float angle = Mathf.Lerp(startAngle, endAngle, eased);

        // Position along the arc relative to the owner's facing.
        Quaternion radial = owner.rotation * Quaternion.Euler(0f, angle, 0f);
        Vector3 dir = radial * Vector3.forward;
        transform.position = owner.position + dir * radius + Vector3.up * verticalOffset;

        // Orient the model: pitch/roll lay it flat, yaw aims the blade along the swing tangent.
        // The yaw sign flips for opposite-direction swings so the blade always leads.
        float effectiveYaw = rightToLeft ? -modelYaw : modelYaw;
        transform.rotation = radial * Quaternion.Euler(modelPitch, effectiveYaw, modelRoll);

        // Hit detection at the sword's current position.
        int n = Physics.OverlapSphereNonAlloc(transform.position, hitRadius, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            Enemy e = hitBuffer[i].GetComponentInParent<Enemy>();
            if (e != null && alreadyHit.Add(e)) e.TakeDamage(damage);
        }

        if (t >= 1f) Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
