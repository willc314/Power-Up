using UnityEngine;

/// <summary>
/// Simple straight-line projectile spawned by ranged enemies.
///
/// Setup checklist on the projectile prefab:
///   * Rigidbody  (Use Gravity = off, Is Kinematic = off)
///   * Collider   (Is Trigger = on)
///   * Tag/Layer  e.g. layer "EnemyProjectile" (so the player layer can collide if you want)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("Travel speed in units per second.")]
    public float speed = 12f;
    [Tooltip("Seconds before the projectile self-destroys if it doesn't hit anything.")]
    public float lifetime = 4f;

    private float damage;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Make sure the projectile's collider is a trigger so OnTriggerEnter fires.
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    public void Launch(Vector3 direction, float damageAmount)
    {
        damage = damageAmount;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) direction.Normalize();
        rb.velocity = direction * speed;
        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Don't hit other enemies or other enemy projectiles.
        if (other.GetComponentInParent<Enemy>() != null) return;
        if (other.GetComponent<EnemyProjectile>() != null) return;

        Hero hero = other.GetComponentInParent<Hero>();
        if (hero != null)
        {
            hero.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        // Hit a wall or environment — also disappear.
        Destroy(gameObject);
    }
}
