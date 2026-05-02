using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boomerang-style shield projectile. Travels outward in a straight line for
/// outboundDuration seconds, then turns around and homes back to the owner
/// (the hero, who can move while the shield is flying). Despawns when it
/// reaches the owner. Damages every enemy it touches once on the outbound
/// trip and once again on the return trip.
/// </summary>
public class ShieldThrow : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Forward speed while flying out, in units/sec.")]
    public float throwSpeed = 14f;
    [Tooltip("Speed while returning to the owner, in units/sec.")]
    public float returnSpeed = 20f;
    [Tooltip("Seconds the shield travels outward before it starts coming back.")]
    public float outboundDuration = 1.0f;
    [Tooltip("Distance from the owner at which the shield is considered 'caught' and despawns.")]
    public float catchDistance = 0.8f;

    [Header("Visual")]
    [Tooltip("Initial rotation applied to the model so it sits flat (parallel to the ground). Try (90,0,0), (-90,0,0), or (90,0,180) until it looks right side up.")]
    public Vector3 modelRotationOffset = new Vector3(-90f, 0f, 0f);
    [Tooltip("Spin speed while flying, in degrees/sec. Pure visual flair.")]
    public float spinSpeed = 720f;
    [Tooltip("World axis the shield spins around. (0,1,0) = horizontal frisbee spin, (1,0,0) = end-over-end.")]
    public Vector3 spinAxis = Vector3.up;

    [Header("Hit Detection")]
    [Tooltip("Radius of the hit-test sphere around the shield each frame.")]
    public float hitRadius = 0.7f;
    [Tooltip("Vertical offset where the catch point is on the owner (so the return aims at the chest, not the feet).")]
    public float ownerCatchHeight = 1.0f;

    private Transform owner;
    private float damage;
    private LayerMask enemyLayers;
    private Vector3 direction;

    private float timer;
    private bool returning;

    // Separate hit sets so the same enemy can be damaged once outbound, once on return.
    private readonly HashSet<Enemy> hitOutbound = new HashSet<Enemy>();
    private readonly HashSet<Enemy> hitReturn = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[16];

    public void Init(Transform owner, float damage, LayerMask enemyLayers, Vector3 direction)
    {
        this.owner = owner;
        this.damage = damage;
        this.enemyLayers = enemyLayers;
        direction.y = 0f;
        this.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        timer = 0f;
        returning = false;

        // Lay the shield flat for a frisbee-style throw.
        transform.rotation = transform.rotation * Quaternion.Euler(modelRotationOffset);
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (!returning)
        {
            transform.position += direction * throwSpeed * Time.deltaTime;
            if (timer >= outboundDuration) returning = true;
        }
        else
        {
            if (owner == null)
            {
                // Hero died or was destroyed — just disappear.
                Destroy(gameObject);
                return;
            }

            Vector3 catchPoint = owner.position + Vector3.up * ownerCatchHeight;
            Vector3 toOwner = catchPoint - transform.position;
            float dist = toOwner.magnitude;

            if (dist <= catchDistance)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += (toOwner / dist) * returnSpeed * Time.deltaTime;
        }

        // Spin for visual flair.
        if (spinSpeed != 0f && spinAxis.sqrMagnitude > 0.0001f)
            transform.Rotate(spinAxis.normalized, spinSpeed * Time.deltaTime, Space.World);

        // Damage enemies the shield is currently overlapping.
        var hitSet = returning ? hitReturn : hitOutbound;
        int n = Physics.OverlapSphereNonAlloc(transform.position, hitRadius, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            Enemy e = hitBuffer[i].GetComponentInParent<Enemy>();
            if (e != null && hitSet.Add(e)) e.TakeDamage(damage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
