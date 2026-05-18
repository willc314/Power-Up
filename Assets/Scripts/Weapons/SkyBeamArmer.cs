using UnityEngine;

/// <summary>
/// Per-arrow marker attached to Heavenly Gale barrage arrows. On each
/// enemy hit, rolls <see cref="chancePerHit"/> — on success, calls back
/// to the source <see cref="BowWeapon"/> to spawn a sky death beam on
/// the enemy that was just struck. Doesn't disarm on success: a piercing
/// arrow can independently roll on every enemy in its chain.
///
/// Mirrors the <see cref="ShivArmer"/> / <see cref="MeteorArmer"/> pattern
/// so the projectile-side hit code stays generic — Projectile.cs just
/// looks up the optional component and forwards the hit; the per-augment
/// logic lives back on the firing weapon.
/// </summary>
public class SkyBeamArmer : MonoBehaviour
{
    [System.NonSerialized] public BowWeapon source;
    [System.NonSerialized] public Hero owner;
    [System.NonSerialized] public float chancePerHit;
    private bool consumed;

    /// <summary>
    /// Called by Projectile / DaggerStab / etc. on each enemy hit. Rolls
    /// chancePerHit; on success, asks the source bow to spawn a sky beam
    /// on this specific enemy AND disarms so the same arrow can't spawn
    /// multiple beams across a piercing chain. With pierce locked at 0
    /// for HG arrows this is a contract guarantee rather than an
    /// observable behavior change, but it keeps the per-arrow cap in
    /// place if future tuning re-introduces pierce.
    /// </summary>
    public void OnEnemyHit(Enemy enemy)
    {
        if (consumed) return;
        if (enemy == null || enemy.IsDead) return;
        if (source == null || owner == null) return;
        if (chancePerHit <= 0f) return;
        if (Random.value >= chancePerHit) return;
        consumed = true;
        source.SpawnHeavenlyGaleSkyBeamOn(owner, enemy);
    }
}
