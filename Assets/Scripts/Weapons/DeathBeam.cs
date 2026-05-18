using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bright beam fired from the hero. Lasts duration seconds, follows the
/// hero's facing (briefly controllable — the player can rotate during the
/// beam), pierces everything in a long thin capsule, and shakes the camera
/// while it's active. The hero is locked in place while firing.
///
/// The visual is whatever model is on this prefab (e.g. a stretched cylinder).
/// The script positions it as a long box from the hero forward, scaled to
/// `range` length and `radius * 2` thickness.
/// </summary>
public class DeathBeam : MonoBehaviour
{
    [Header("Beam")]
    [Tooltip("How long the beam stays active.")]
    public float duration = 1.5f;
    [Tooltip("Length of the beam in world units.")]
    public float range = 30f;
    [Tooltip("Visual radius of the beam — scales the visual cube on X and Z.")]
    public float radius = 0.6f;
    [Tooltip("Multiplier on the visual radius for hit detection. >1 makes the beam damage enemies in the bloom halo around the visible beam, not just inside it. 2.5 is a good starting point for noticeable bloom.")]
    public float hitRadiusMultiplier = 2.5f;
    [Tooltip("Damage dealt per second to each enemy in the beam. Boosted by the bow's powerups (deathBeamDpsBonus, post-overcharge multiplier, hero damage modifiers).")]
    public float damagePerSecond = 200f;
    [Tooltip("ADDITIONAL damage dealt per second to each enemy, expressed as a fraction of THAT enemy's MAX HP. Stacks ON TOP of damagePerSecond — does NOT replace it. Fixed — no powerup, no post-overcharge multiplier, no hero damage modifier touches this. 0.01 = an extra 1%/s of max HP. Set to 0 to disable the bonus damage entirely.")]
    [Range(0f, 1f)] public float maxHpFractionPerSecond = 0.01f;

    [Header("Hero Control")]
    [Tooltip("Hero speed multiplier while the beam is active. 0 = locked in place.")]
    [Range(0f, 1f)] public float heroSpeedDuringBeam = 0f;

    [Header("Camera")]
    [Tooltip("Camera shake amplitude while the beam is active.")]
    public float shakeAmplitude = 0.5f;

    [Header("VFX")]
    [Tooltip("Optional particle prefab spawned at the beam origin (parented to the hero).")]
    public GameObject vfxPrefab;
    [Tooltip("Vertical offset above the hero pivot the beam fires from.")]
    public float spawnHeight = 1.0f;

    [Header("Fixed Mode (Heavenly Gale sky beams)")]
    [Tooltip("If true, the beam DOESN'T follow the owner — its position and rotation are set once at Init and stay put. Used by Heavenly Gale's sky-beam variant which spawns vertical above an enemy and stays in place. Damage capsule still ticks every frame.")]
    public bool useFixedTransform = false;
    [Tooltip("Color tint applied to every renderer in the spawned beam at Init time. Alpha = 1 means full overwrite of the prefab's authored color; alpha 0 leaves the prefab untouched. Used by Heavenly Gale to give each sky beam a slightly different hue.")]
    public Color tintColor = new Color(1f, 1f, 1f, 0f);
    [Tooltip("Multiplier on tintColor when applied to the renderer's emission color. Values > 1 push emission into HDR territory so the post-process bloom kicks in — the visible color (base/tint) stays the same hue, but the beam emits brighter and bloom takes over from there. 1 = no bloom boost. 4-8 = punchy bloom.")]
    public float emissionBloomIntensity = 1f;

    [Header("SFX")]
    [Tooltip("One-shot sound played at the beam's origin when the beam first fires. Routed through SoundManager so it picks up the global SFX volume + 3D rolloff. Leave null for silent beams.")]
    public AudioClip fireSound;
    [Tooltip("Per-clip volume multiplier for the fire SFX. Stacks on top of SoundManager.volume.")]
    [Range(0f, 1f)] public float fireSoundVolume = 1f;

    [Header("Trail Particles")]
    [Tooltip("How many particles per second flow backward from the beam toward the player. 0 = none.")]
    public float beamParticlesPerSecond = 80f;
    [Tooltip("Color of the trail particles.")]
    public Color beamParticleColor = Color.white;
    [Tooltip("Speed each trail particle starts at, flowing back toward the player.")]
    public float beamParticleSpeed = 12f;
    [Tooltip("Particle lifetime.")]
    public float beamParticleLifetime = 0.35f;
    [Tooltip("Edge length of each particle cube.")]
    public float beamParticleSize = 0.1f;
    [Tooltip("Cone spread angle in degrees.")]
    public float beamParticleSpread = 12f;

    private Hero owner;
    private LayerMask enemyLayers;
    private float timer;
    private GameObject vfxInstance;
    private CameraFollow cam;
    private readonly Collider[] hitBuffer = new Collider[64];
    private float particleAccumulator;

    public void Init(Hero owner, LayerMask enemyLayers)
    {
        this.owner = owner;
        this.enemyLayers = enemyLayers;
        timer = 0f;
        // Speed lock only applies to OWNER-FOLLOW beams (the regular bow's
        // death beam where the hero stands still while firing). Fixed-mode
        // sky beams (Heavenly Gale) don't touch hero movement — multiple
        // can fire concurrently and the hero needs to keep moving while
        // they tick down on enemies.
        if (!useFixedTransform)
            owner.speedMultiplier = heroSpeedDuringBeam;

        // Disable any colliders on the visual so the stretched box doesn't physically shove the hero.
        // Damage is handled by the OverlapCapsule call in Update, not by physics collisions.
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

        cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;

        if (vfxPrefab != null)
        {
            vfxInstance = Instantiate(vfxPrefab, owner.transform.position + Vector3.up * spawnHeight, owner.transform.rotation, owner.transform);
        }

        // One-shot fire SFX at the beam's origin — same SoundManager pipeline
        // the meteor uses, so it inherits the global SFX volume slider and
        // 3D rolloff. Plays once at Init, not looped, so a long beam plays
        // its "firing" sound at the start and the rest is just visuals +
        // damage ticks.
        if (fireSound != null && SoundManager.Instance != null)
        {
            Vector3 sfxPos = owner.transform.position + Vector3.up * spawnHeight;
            SoundManager.Instance.PlaySfxAt(fireSound, sfxPos, fireSoundVolume);
        }

        // Visual scale: stretch X/Z to thickness, Y to a thin slab, length on Z.
        // We scale the model so a 1-unit cube becomes a beam of (radius*2) thickness and `range` length.
        transform.localScale = new Vector3(radius * 2f, radius * 2f, range);

        // Optional runtime color tint — used by Heavenly Gale to give each
        // sky beam a slightly different hue. tintColor.a > 0 means we want
        // to override; alpha == 0 means leave the authored material alone.
        if (tintColor.a > 0.001f)
            ApplyTintToRenderers(tintColor);
    }

    /// <summary>
    /// Walk every Renderer on the beam and replace each material's color
    /// (and emission color where supported) with <paramref name="tint"/>.
    /// Emission gets multiplied by emissionBloomIntensity so the visible
    /// hue matches tint while the emission can sit in HDR territory for
    /// post-process bloom. MaterialPropertyBlock keeps the change
    /// instance-local so other active beams sharing the same material
    /// aren't recolored too.
    /// </summary>
    private void ApplyTintToRenderers(Color tint)
    {
        var block = new MaterialPropertyBlock();
        // Emission color carries the bloom boost. Alpha is preserved from
        // the input tint so HDR'ing emission doesn't change opacity.
        float boost = Mathf.Max(0f, emissionBloomIntensity);
        Color emission = new Color(tint.r * boost, tint.g * boost, tint.b * boost, tint.a);
        foreach (var rend in GetComponentsInChildren<Renderer>(true))
        {
            rend.GetPropertyBlock(block);
            if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_Color"))
                block.SetColor("_Color", tint);
            if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_BaseColor"))
                block.SetColor("_BaseColor", tint);
            if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_EmissionColor"))
                block.SetColor("_EmissionColor", emission);
            if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_TintColor"))
                block.SetColor("_TintColor", tint);
            rend.SetPropertyBlock(block);
        }
    }

    private void Update()
    {
        // In owner-follow mode, the beam dies if the owner does — keeps
        // the visual from tracking a destroyed hero. In fixed mode the
        // beam owns its position and stays alive even if the owner died
        // (useful for Heavenly Gale sky beams that should resolve their
        // damage window even if the hero gets killed mid-cast).
        if (!useFixedTransform && (owner == null || owner.IsDead))
        {
            Cleanup();
            Destroy(gameObject);
            return;
        }

        timer += Time.deltaTime;

        // Position: in owner-follow mode, track the hero each frame; in
        // fixed mode, leave whatever transform was set externally before
        // Init. Damage capsule endpoints are derived from the same
        // forward axis either way.
        Vector3 origin;
        Vector3 fwd;
        if (useFixedTransform)
        {
            // The caller positioned the transform pre-Init at the beam's
            // ORIGIN with rotation pointing along the beam's forward axis.
            // We don't override that here — but we do compute the damage
            // capsule endpoints from origin + forward, NOT from transform.position
            // (which is centered at range/2 from origin).
            origin = transform.position - transform.forward * (range * 0.5f);
            fwd = transform.forward;
        }
        else
        {
            origin = owner.transform.position + Vector3.up * spawnHeight;
            fwd = owner.transform.forward;
            // Center the beam halfway down its length so the box sits in front of the hero.
            transform.position = origin + fwd * (range * 0.5f);
            transform.rotation = owner.transform.rotation;
        }

        // Damage tick — capsule from origin to origin + forward * range.
        // Hit radius is independent from the visual radius so the beam can damage
        // enemies inside the bloom halo, not just inside the visible cube.
        float hitRadius = radius * Mathf.Max(0.1f, hitRadiusMultiplier);
        Vector3 p1 = origin + fwd * radius;
        Vector3 p2 = origin + fwd * (range - radius);
        int n = Physics.OverlapCapsuleNonAlloc(p1, p2, hitRadius, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);

        // Two-track damage: the boosted flat DPS (Damage / Projectiles boosts,
        // post-overcharge multiplier, hero modifiers — already baked in by
        // BowWeapon.FireDeathBeam), PLUS a fixed %-of-max-HP-per-second tick
        // that bypasses every multiplier. Both are applied as separate
        // TakeDamage calls per enemy per frame.
        float flatDmg = damagePerSecond * Time.deltaTime;
        HashSet<Enemy> hitOnce = new HashSet<Enemy>();
        for (int i = 0; i < n; i++)
        {
            Enemy e = hitBuffer[i].GetComponentInParent<Enemy>();
            if (e == null || !hitOnce.Add(e)) continue;
            // Base flat damage + bonuses (boostable).
            if (flatDmg > 0f) e.TakeDamage(flatDmg);
            // % of max HP — fixed, applied additionally so the beam is
            // meaningfully effective against very high-HP bosses.
            if (maxHpFractionPerSecond > 0f && !e.IsDead)
            {
                float maxHpDmg = (float)e.MaxHP * maxHpFractionPerSecond * Time.deltaTime;
                if (maxHpDmg > 0f) e.TakeDamage(maxHpDmg);
            }
        }

        // Continuous camera shake.
        if (cam != null && shakeAmplitude > 0f) cam.Shake(shakeAmplitude, 0.1f);

        // Trail particles flowing backward (toward the hero) along the beam.
        EmitTrailParticles(origin);

        if (timer >= duration) { Cleanup(); Destroy(gameObject); }
    }

    private void EmitTrailParticles(Vector3 origin)
    {
        if (beamParticlesPerSecond <= 0f) return;
        particleAccumulator += beamParticlesPerSecond * Time.deltaTime;
        Vector3 backward = -owner.transform.forward;
        while (particleAccumulator >= 1f)
        {
            // Pick a random point along the beam — not too close to the hero, not at the very end.
            float along = Random.Range(radius * 2f, range);
            Vector3 spawnPoint = origin + owner.transform.forward * along
                                 + Random.insideUnitSphere * radius * 0.5f;
            HitParticles.EmitBurst(spawnPoint, backward,
                count: 1,
                speed: beamParticleSpeed,
                lifetime: beamParticleLifetime,
                size: beamParticleSize,
                color: beamParticleColor,
                spreadAngle: beamParticleSpread,
                useGravity: false);
            particleAccumulator -= 1f;
        }
    }

    private void Cleanup()
    {
        // Only restore the hero's speedMultiplier if WE took control of it
        // in Init. Fixed-mode sky beams never touched it, so resetting to
        // 1f here would clobber other systems' active slows (e.g. a different
        // weapon's charging slow running concurrently).
        if (!useFixedTransform && owner != null) owner.speedMultiplier = 1f;
        if (vfxInstance != null) Destroy(vfxInstance, 0.3f);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 fwd = Application.isPlaying && owner != null ? owner.transform.forward : transform.forward;
        Vector3 origin = (Application.isPlaying && owner != null ? owner.transform.position : transform.position) + Vector3.up * spawnHeight;

        // Visual beam outline.
        Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
        Gizmos.DrawWireSphere(origin, radius);
        Gizmos.DrawWireSphere(origin + fwd * range, radius);
        Gizmos.DrawLine(origin, origin + fwd * range);

        // Hit volume (always at least as big as the visual).
        float hitRadius = radius * Mathf.Max(0.1f, hitRadiusMultiplier);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(origin, hitRadius);
        Gizmos.DrawWireSphere(origin + fwd * range, hitRadius);
    }
}
