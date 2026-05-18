using UnityEngine;

/// <summary>
/// In-flight grenade. Travels in a parabolic arc to a target point chosen by
/// GrenadeWeapon, lands, and triggers an Explosion at its position.
/// Tumbles for visual flair while in the air.
/// </summary>
public class Grenade : MonoBehaviour
{
    [Header("Detonation")]
    [Tooltip("Prefab spawned at the landing point. Should have an Explosion component.")]
    public Explosion explosionPrefab;

    [Header("Visual")]
    [Tooltip("Tumble speed while in flight, deg/sec.")]
    public float tumbleSpeed = 360f;
    [Tooltip("Random tumble axis, used as-is (will be normalized).")]
    public Vector3 tumbleAxis = new Vector3(1f, 0.5f, 0.3f);

    private Vector3 startPos;
    private Vector3 endPos;
    private float arcHeight;
    private float duration;
    private float timer;

    /// <summary>
    /// Extra radius added to the spawned Explosion's radius. Set by
    /// GrenadeWeapon when the grenade has been upgraded with the +range boost.
    /// </summary>
    [System.NonSerialized] public float radiusBonus = 0f;

    /// <summary>
    /// Final explosion damage to use, after the GrenadeWeapon has applied
    /// hero damage modifiers (general damage + crit roll). Set to 0 or below
    /// to keep the Explosion prefab's own damage value.
    /// </summary>
    [System.NonSerialized] public float damageOverride = 0f;

    /// <summary>
    /// Pre-crit damage value forwarded to <see cref="Explosion.friendlyFireDamage"/>
    /// so a player crit doesn't amplify the hero's own self-damage when they
    /// stand in their grenade's AOE. Set to 0 or below to fall back to the
    /// legacy "damage * 0.5f" behavior.
    /// </summary>
    [System.NonSerialized] public float friendlyFireDamage = 0f;

    public void Launch(Vector3 startPos, Vector3 endPos, float arcHeight, float flightTime)
    {
        this.startPos = startPos;
        this.endPos = endPos;
        this.arcHeight = arcHeight;
        this.duration = Mathf.Max(0.05f, flightTime);
        timer = 0f;
        transform.position = startPos;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        // Parabolic interpolation: linear xz + quadratic y.
        Vector3 pos = Vector3.Lerp(startPos, endPos, t);
        pos.y += 4f * arcHeight * t * (1f - t); // peaks at t=0.5
        transform.position = pos;

        if (tumbleSpeed != 0f && tumbleAxis.sqrMagnitude > 0.0001f)
            transform.Rotate(tumbleAxis.normalized, tumbleSpeed * Time.deltaTime, Space.World);

        if (t >= 1f) Detonate();
    }

    private void Detonate()
    {
        if (explosionPrefab != null)
        {
            Explosion ex = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            // Apply the upgrade-driven radius bonus before the AOE damage pass.
            if (radiusBonus > 0f) ex.radius += radiusBonus;
            // Hero damage modifiers are baked into damageOverride by GrenadeWeapon.
            if (damageOverride > 0f) ex.damage = damageOverride;
            // Pre-crit friendly-fire damage so the player's own crit doesn't
            // amplify self-damage when they stand in the AOE. Forwarded as-is
            // to Explosion which uses it instead of damage * 0.5f.
            if (friendlyFireDamage > 0f) ex.friendlyFireDamage = friendlyFireDamage;
            // Meteor general augment: transfer our armer (if any) onto the
            // Explosion's GameObject so it fires on the first enemy actually
            // caught by the AOE — gated on enemy hit, matching the pattern
            // every other weapon uses. If the AOE catches nothing the
            // armer just dies with the Explosion, no wasted meteor.
            var armer = GetComponent<MeteorArmer>();
            if (armer != null && armer.IsArmed)
                MeteorArmer.Transfer(armer, ex.gameObject);
            ex.Detonate();
        }
        Destroy(gameObject);
    }
}
