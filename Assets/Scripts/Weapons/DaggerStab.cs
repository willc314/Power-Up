using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A quick forward thrust. Spawns a small dagger model in front of the hero,
/// extends it briefly along the hero's forward axis, hits whatever it touches,
/// then despawns. Much faster and shorter range than a sword swing.
/// </summary>
public class DaggerStab : MonoBehaviour
{
    [Header("Thrust")]
    [Tooltip("Distance the stab extends forward from the start position.")]
    public float thrustDistance = 0.6f;
    [Tooltip("Total time of the stab animation (out + in).")]
    public float duration = 0.15f;
    [Tooltip("Vertical offset above the hero pivot.")]
    public float verticalOffset = 1.0f;

    [Header("Hit Volume")]
    [Tooltip("Length of the hit capsule (the blade).")]
    public float bladeLength = 0.8f;
    [Tooltip("Thickness of the hit capsule.")]
    public float bladeRadius = 0.2f;
    [Tooltip("Local axis the blade points along. Default Y matches the included Dagger.prefab.")]
    public SwordSlash.BladeAxis bladeAxis = SwordSlash.BladeAxis.Y;
    [Tooltip("Local-space center of the hit capsule relative to the model pivot.")]
    public Vector3 bladeCenterOffset = new Vector3(0f, 0.4f, 0f);

    [Header("Visual")]
    [Tooltip("Extra Euler rotation to lay the dagger flat / point it forward.")]
    public Vector3 modelRotationOffset = new Vector3(90f, 0f, 0f);
    [Tooltip("Draw the hit capsule in the Scene view at runtime.")]
    public bool debugDrawHitbox = false;

    private Transform owner;
    private float damage;
    private LayerMask enemyLayers;
    private float baseForwardOffset; // distance in front the stab starts at
    private float timer;
    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[16];

    public void Init(Transform owner, float damage, LayerMask enemyLayers, float startForwardOffset)
    {
        this.owner = owner;
        this.damage = damage;
        this.enemyLayers = enemyLayers;
        baseForwardOffset = startForwardOffset;
        timer = 0f;
        Destroy(gameObject, duration + 0.05f);
    }

    private void Update()
    {
        if (owner == null) { Destroy(gameObject); return; }
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        // Triangle envelope: extend out (0..0.5) then retract (0.5..1).
        float extension = (t < 0.5f) ? (t * 2f) : ((1f - t) * 2f);

        Vector3 forwardWorld = owner.forward;
        float forwardDist = baseForwardOffset + extension * thrustDistance;
        transform.position = owner.position + forwardWorld * forwardDist + Vector3.up * verticalOffset;
        transform.rotation = owner.rotation * Quaternion.Euler(modelRotationOffset);

        // Hit detection along the blade.
        Vector3 axisLocal = SwordSlash_AxisVector(bladeAxis);
        Vector3 axisWorld = transform.TransformDirection(axisLocal);
        Vector3 center = transform.TransformPoint(bladeCenterOffset);
        float halfLen = Mathf.Max(0f, bladeLength * 0.5f - bladeRadius);
        Vector3 p1 = center - axisWorld * halfLen;
        Vector3 p2 = center + axisWorld * halfLen;

        int n = Physics.OverlapCapsuleNonAlloc(p1, p2, bladeRadius, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            Enemy e = hitBuffer[i].GetComponentInParent<Enemy>();
            if (e != null && alreadyHit.Add(e)) e.TakeDamage(damage);
        }

        if (debugDrawHitbox) Debug.DrawLine(p1, p2, Color.cyan);

        if (t >= 1f) Destroy(gameObject);
    }

    private static Vector3 SwordSlash_AxisVector(SwordSlash.BladeAxis a)
    {
        switch (a)
        {
            case SwordSlash.BladeAxis.X: return Vector3.right;
            case SwordSlash.BladeAxis.Y: return Vector3.up;
            case SwordSlash.BladeAxis.Z: return Vector3.forward;
        }
        return Vector3.up;
    }
}
