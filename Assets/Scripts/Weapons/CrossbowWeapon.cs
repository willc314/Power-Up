using UnityEngine;

/// <summary>
/// Fires straight arrows at moderate speed on every press / cooldown tick.
/// While the fire button is held, a crossbow visual sits in front of the hero.
/// </summary>
public class CrossbowWeapon : Weapon
{
    [Header("Crossbow")]
    [Tooltip("Prefab spawned per shot. Should have a Projectile component on its root, ideally wrapping Arrow_Regular.")]
    public Projectile arrowPrefab;
    [Tooltip("How far in front of the hero the arrow spawns.")]
    public float spawnDistance = 0.8f;
    [Tooltip("Vertical offset above the hero's pivot.")]
    public float spawnHeight = 1.0f;

    [Header("Crossbow Visual")]
    [Tooltip("Prefab shown in the hero's hands while holding the fire button (e.g. Crossbow.prefab). Despawned on release.")]
    public GameObject crossbowVisualPrefab;
    [Tooltip("How far in front of the hero the visual sits.")]
    public float visualSpawnDistance = 1.0f;
    [Tooltip("Vertical offset for the visual.")]
    public float visualSpawnHeight = 1.0f;
    [Tooltip("Extra Euler rotation so the crossbow points along the hero's forward. Try (0,-90,0), (0,90,0), or (0,180,0) until it points right.")]
    public Vector3 visualRotationOffset = new Vector3(0f, -90f, 0f);

    private GameObject visualInstance;

    public override bool OnFireDown(Hero owner)
    {
        SpawnVisual(owner);
        return false;
    }

    public override bool OnFireHeld(Hero owner)
    {
        UpdateVisualPose(owner);
        return TryFire(owner); // fires arrows on cooldown while held
    }

    public override bool OnFireUp(Hero owner)
    {
        DespawnVisual();
        return false;
    }

    protected override void Fire(Hero owner)
    {
        if (arrowPrefab == null) { Debug.LogWarning("CrossbowWeapon: Arrow Prefab not assigned."); return; }
        Vector3 spawn = owner.transform.position
                      + owner.transform.forward * spawnDistance
                      + Vector3.up * spawnHeight;
        Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
        p.Launch(owner.transform.forward, damage, enemyLayers);
    }

    private void SpawnVisual(Hero owner)
    {
        if (crossbowVisualPrefab == null) return;
        if (visualInstance != null) Destroy(visualInstance);
        Vector3 pos = owner.transform.position
                    + owner.transform.forward * visualSpawnDistance
                    + Vector3.up * visualSpawnHeight;
        visualInstance = Instantiate(crossbowVisualPrefab, pos,
            owner.transform.rotation * Quaternion.Euler(visualRotationOffset));
        // Make sure no collider on the visual shoves the hero around.
        foreach (var c in visualInstance.GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    private void UpdateVisualPose(Hero owner)
    {
        if (visualInstance == null) return;
        visualInstance.transform.position = owner.transform.position
            + owner.transform.forward * visualSpawnDistance
            + Vector3.up * visualSpawnHeight;
        visualInstance.transform.rotation = owner.transform.rotation * Quaternion.Euler(visualRotationOffset);
    }

    private void DespawnVisual()
    {
        if (visualInstance != null) Destroy(visualInstance);
        visualInstance = null;
    }

    private void OnDisable() { DespawnVisual(); }
}
