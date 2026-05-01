using UnityEngine;

/// <summary>
/// Primary melee weapon: spawns a large sword in front of the hero that
/// sweeps across an arc. Alternates between left-to-right and right-to-left
/// swings each time it fires, so a quick double-tap looks like a 1-2 combo.
/// </summary>
public class SwordWeapon : Weapon
{
    [Header("Sword")]
    [Tooltip("Prefab spawned for each swing. Should have a SwordSlash component on its root, ideally wrapping a sword model (e.g. OHS03Polyart).")]
    public SwordSlash slashPrefab;
    [Tooltip("How far in front of the hero the swing centers.")]
    public float spawnDistance = 1.0f;
    [Tooltip("Vertical offset above the hero's pivot so the swing reads at chest/shoulder height.")]
    public float spawnHeight = 1.0f;

    // Toggles each fire so successive swings come from opposite sides.
    private bool nextSwingRightToLeft = false;

    protected override void Fire(Hero owner)
    {
        if (slashPrefab == null)
        {
            Debug.LogWarning("SwordWeapon: Slash Prefab is not assigned.");
            return;
        }

        Vector3 spawnPos = owner.transform.position
                         + owner.transform.forward * spawnDistance
                         + Vector3.up * spawnHeight;

        // Parent to the owner so the slash follows the hero's facing while it sweeps.
        SwordSlash slash = Instantiate(slashPrefab, spawnPos, owner.transform.rotation, owner.transform);
        slash.Init(owner.transform, damage, enemyLayers, nextSwingRightToLeft);
        nextSwingRightToLeft = !nextSwingRightToLeft;
    }
}
