using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic straight-line projectile used by Crossbow (Arrow_Regular), Bow
/// (Arrow_Piercing), and Dagger (thrown). Travels at fixed speed in its
/// initial direction, damages enemies on contact, optionally pierces, and
/// optionally spins for visual flair.
/// </summary>
public class Projectile : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Travel speed in units/sec.")]
    public float speed = 20f;
    [Tooltip("Seconds before the projectile self-destroys if it doesn't hit anything.")]
    public float lifetime = 4f;
    [Tooltip("Max enemies the projectile passes through. 0 = stop on first hit; -1 = pierces forever.")]
    public int pierceCount = 0;

    [Header("Hit Volume")]
    [Tooltip("Thickness of the hit volume swept along the projectile's path each frame.")]
    public float hitRadius = 0.3f;
    [Tooltip("Vertical height of the hit volume. Use a tall value (1.5-2.5) so projectiles catch short enemies whose colliders sit below the projectile's flight height. If <= 2*hitRadius, it behaves as a pure sphere.")]
    public float hitHeight = 2.0f;

    [Header("Visual")]
    [Tooltip("Spin around the projectile's local forward axis, deg/sec. 0 = no spin (arrows). Use 720+ for thrown daggers.")]
    public float spinSpeed = 0f;
    [Tooltip("Initial Euler offset applied so the model points along travel. Tweak if the arrow/dagger model points the wrong way (try (90,0,0), (0,90,0), or (0,0,90)).")]
    public Vector3 modelRotationOffset = Vector3.zero;

    private Vector3 direction;
    private float damage;
    private LayerMask enemyLayers;
    private int pierceRemaining;
    private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();
    private readonly Collider[] hitBuffer = new Collider[16];

    public void Launch(Vector3 dir, float damage, LayerMask enemyLayers)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        this.damage = damage;
        this.enemyLayers = enemyLayers;
        this.pierceRemaining = pierceCount;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(modelRotationOffset);
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        Vector3 step = direction * speed * Time.deltaTime;
        Vector3 nextPos = transform.position + step;

        // Vertical capsule for hit detection — covers from foot height to head height
        // so short enemies aren't missed by an arrow flying at chest level.
        float halfExtent = Mathf.Max(0f, hitHeight * 0.5f - hitRadius);
        Vector3 p1 = transform.position + Vector3.up * halfExtent;
        Vector3 p2 = transform.position - Vector3.up * halfExtent;

        if (Physics.CapsuleCast(p1, p2, hitRadius, direction, out RaycastHit hit, step.magnitude + 0.01f, enemyLayers, QueryTriggerInteraction.Collide))
        {
            Enemy e = hit.collider.GetComponentInParent<Enemy>();
            if (e != null && alreadyHit.Add(e))
            {
                e.TakeDamage(damage);
                if (pierceCount >= 0 && pierceRemaining-- <= 0)
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        transform.position = nextPos;

        if (spinSpeed != 0f) transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
        float halfExtent = Mathf.Max(0f, hitHeight * 0.5f - hitRadius);
        Vector3 p1 = transform.position + Vector3.up * halfExtent;
        Vector3 p2 = transform.position - Vector3.up * halfExtent;
        Gizmos.DrawWireSphere(p1, hitRadius);
        Gizmos.DrawWireSphere(p2, hitRadius);
        Gizmos.DrawLine(p1 + Vector3.right * hitRadius, p2 + Vector3.right * hitRadius);
        Gizmos.DrawLine(p1 - Vector3.right * hitRadius, p2 - Vector3.right * hitRadius);
        Gizmos.DrawLine(p1 + Vector3.forward * hitRadius, p2 + Vector3.forward * hitRadius);
        Gizmos.DrawLine(p1 - Vector3.forward * hitRadius, p2 - Vector3.forward * hitRadius);
    }
}
