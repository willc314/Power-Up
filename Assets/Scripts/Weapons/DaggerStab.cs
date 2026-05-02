using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One quick dagger stab.
/// Goes on the root of the DaggerStab prefab.
///
/// The visual dagger extends forward briefly and then retracts.
/// Damage is handled by a forward capsule cast in front of the Hero, not by
/// relying on the exact model blade orientation. This makes the dagger more
/// reliable even if the dagger model points along a strange local axis.
///
/// Each enemy is hit at most once per stab.
/// </summary>
public class DaggerStab : MonoBehaviour
{
    [Header("Thrust Visual")]
    [Tooltip("Distance the dagger visual extends forward from the start position.")]
    public float thrustDistance = 0.7f;

    [Tooltip("Total time of the stab animation, including out and back.")]
    public float duration = 0.16f;

    [Tooltip("Vertical offset above the Hero pivot.")]
    public float verticalOffset = 1.0f;

    [Header("Gameplay Hitbox")]
    [Tooltip("How far forward the stab can hit.")]
    public float hitRange = 1.8f;

    [Tooltip("Radius of the forward stab hit capsule.")]
    public float hitRadius = 0.45f;

    [Tooltip("Vertical center of the stab hitbox above the Hero pivot.")]
    public float hitHeight = 1.0f;

    [Tooltip("Vertical thickness of the stab hitbox. Larger values hit short/tall enemies more reliably.")]
    public float hitVerticalSize = 1.3f;

    [Tooltip("Draw the stab hit capsule in the Scene view at runtime.")]
    public bool debugDrawHitbox = true;

    [Header("Visual Rotation")]
    [Tooltip("Extra Euler rotation to make the dagger model point forward.")]
    public Vector3 modelRotationOffset = new Vector3(90f, 0f, 0f);

    private Transform owner;
    private float damage;
    private LayerMask enemyLayers;
    private float baseForwardOffset;
    private float timer;

    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[24];

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
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        UpdateVisual(t);
        DamageUsingForwardStab();

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void UpdateVisual(float t)
    {
        float extension = t < 0.5f
            ? t * 2f
            : (1f - t) * 2f;

        float forwardDistance = baseForwardOffset + extension * thrustDistance;

        transform.position = owner.position
                           + owner.forward * forwardDistance
                           + Vector3.up * verticalOffset;

        transform.rotation = owner.rotation * Quaternion.Euler(modelRotationOffset);
    }

    private void DamageUsingForwardStab()
    {
        Vector3 center = owner.position + Vector3.up * hitHeight;
        Vector3 forward = owner.forward;

        float halfVertical = Mathf.Max(0.05f, hitVerticalSize * 0.5f);

        Vector3 p1 = center + Vector3.up * halfVertical;
        Vector3 p2 = center - Vector3.up * halfVertical;

        int n = Physics.CapsuleCastNonAlloc(
            p1,
            p2,
            hitRadius,
            forward,
            hitBuffer,
            hitRange,
            enemyLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < n; i++)
        {
            Collider hitCollider = hitBuffer[i].collider;

            if (hitCollider == null)
                continue;

            Enemy enemy = hitCollider.GetComponentInParent<Enemy>();

            if (enemy != null && alreadyHit.Add(enemy))
                enemy.TakeDamage(damage);
        }

        if (debugDrawHitbox)
        {
            Debug.DrawLine(p1, p1 + forward * hitRange, Color.magenta);
            Debug.DrawLine(p2, p2 + forward * hitRange, Color.magenta);
        }
    }
}