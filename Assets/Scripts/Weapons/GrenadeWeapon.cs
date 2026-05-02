using UnityEngine;

/// <summary>
/// Slowly throws an explosive grenade that lands a fixed distance in front of
/// the hero and detonates in a large AOE.
/// </summary>
public class GrenadeWeapon : Weapon
{
    [Header("Grenade")]
    [Tooltip("Prefab spawned per throw. Should have a Grenade component.")]
    public Grenade grenadePrefab;
    [Tooltip("World units in front of the hero where the grenade lands.")]
    public float throwDistance = 6f;
    [Tooltip("Peak height of the throw arc.")]
    public float arcHeight = 3f;
    [Tooltip("Seconds the grenade spends in flight.")]
    public float flightTime = 0.8f;
    [Tooltip("Vertical offset for the throw release point.")]
    public float spawnHeight = 1.0f;

    protected override void Fire(Hero owner)
    {
        if (grenadePrefab == null) { Debug.LogWarning("GrenadeWeapon: Grenade Prefab not assigned."); return; }

        Vector3 startPos = owner.transform.position
                         + owner.transform.forward * 0.5f
                         + Vector3.up * spawnHeight;
        Vector3 endPos = owner.transform.position
                         + owner.transform.forward * throwDistance;

        Grenade g = Instantiate(grenadePrefab, startPos, Quaternion.identity);
        g.Launch(startPos, endPos, arcHeight, flightTime);
    }
}
