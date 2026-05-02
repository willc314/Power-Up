using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Charging bow.
///   Hold to charge. The bow visual (a Bow.prefab spawned in front of the
///   hero) glows brighter white and the hero is slowed.
///   Release before overcharge time → fires a piercing arrow whose damage
///   and visual size scale with charge time.
///   Hold past overcharge time → on release, fires a Death Beam: a long
///   bright beam in front of the hero that lasts a brief moment, shakes the
///   camera, and pierces everything.
/// </summary>
public class BowWeapon : Weapon
{
    [Header("Bow Visual")]
    [Tooltip("Prefab spawned in front of the hero while charging (e.g. Bow.prefab). Despawned on release.")]
    public GameObject bowVisualPrefab;
    [Tooltip("How far in front of the hero the bow visual sits.")]
    public float bowSpawnDistance = 1.2f;
    [Tooltip("Vertical offset for the bow visual.")]
    public float bowSpawnHeight = 1.0f;
    [Tooltip("Extra Euler rotation applied to the bow visual so the arrow points along the hero's forward. Try (0,-90,0), (0,90,0), or (0,180,0) until it points the right way.")]
    public Vector3 bowVisualRotationOffset = new Vector3(0f, -90f, 0f);
    [Tooltip("Color the bow blends toward at full charge.")]
    public Color glowColor = Color.white;
    [Tooltip("How brightly the bow emits light at full charge. 0 = no emission, 1-3 reads as a clear glow. With Bloom post-processing enabled, higher values look more 'glowing'.")]
    public float emissionIntensity = 2.5f;
    [Tooltip("If true, also adds a Point Light to the bow that brightens with charge. Use this when the bow's material doesn't support emission (e.g. Mobile/Diffuse shader).")]
    public bool useGlowLight = true;
    [Tooltip("Maximum Light intensity at full charge.")]
    public float maxLightIntensity = 6f;
    [Tooltip("Maximum Light range at full charge.")]
    public float maxLightRange = 5f;

    [Header("Charge")]
    [Tooltip("Seconds to reach full normal-shot charge. After this, damage and arrow size are at max.")]
    public float fullChargeTime = 1.5f;
    [Tooltip("Hero speed multiplier while charging (0 = stuck, 1 = no slowdown).")]
    [Range(0f, 1f)] public float slowdownWhileCharging = 0.4f;
    [Tooltip("Minimum charge time required to actually fire something. Below this, the release does nothing (prevents accidental dry-fires).")]
    public float minReleaseTime = 0.1f;

    [Header("Arrow")]
    [Tooltip("Prefab spawned on release for normal shots. Should have a Projectile component (use Arrow_Piercing.prefab).")]
    public Projectile arrowPrefab;
    [Tooltip("If > 0, overrides the projectile prefab's Speed so bow arrows can fly faster than crossbow arrows. Set to 0 to use the prefab's default.")]
    public float arrowSpeed = 45f;
    [Tooltip("Damage at zero charge.")]
    public float arrowMinDamage = 10f;
    [Tooltip("Damage at full charge (and beyond).")]
    public float arrowMaxDamage = 70f;
    [Tooltip("Visual scale multiplier at zero charge.")]
    public float arrowMinScale = 1f;
    [Tooltip("Visual scale multiplier at full charge.")]
    public float arrowMaxScale = 2.5f;

    [Header("Death Beam (Overcharge)")]
    [Tooltip("Holding past this many seconds switches the release to a Death Beam.")]
    public float overchargeTime = 5f;
    [Tooltip("Prefab spawned on overcharged release. Should have a DeathBeam component on a stretched cube/cylinder.")]
    public DeathBeam deathBeamPrefab;
    [Tooltip("Color the bow blends toward when in overcharge mode (visual cue that the next release is the beam).")]
    public Color overchargeColor = new Color(1f, 0.95f, 0.7f);

    [Header("Audio / VFX (optional)")]
    [Tooltip("Optional particle prefab spawned on the bow when entering overcharge.")]
    public GameObject overchargeVfxPrefab;

    // ---- Runtime state ----
    private bool charging;
    private float chargeTime;
    private GameObject bowVisualInstance;
    private GameObject overchargeVfxInstance;
    private Light glowLight;
    private bool overchargeReached;

    // Cached material info for tinting the bow visual.
    private struct MatRef { public Material mat; public int prop; public Color original; }
    private readonly List<MatRef> bowMats = new List<MatRef>();
    private static readonly int kBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int kColor     = Shader.PropertyToID("_Color");
    private static readonly int kEmission  = Shader.PropertyToID("_EmissionColor");

    public override bool CanFire => true; // charging weapon — always allowed to start

    public override bool OnFireDown(Hero owner)
    {
        StartCharge(owner);
        return false;
    }

    public override bool OnFireHeld(Hero owner)
    {
        if (!charging) return false;
        chargeTime += Time.deltaTime;
        owner.speedMultiplier = slowdownWhileCharging;

        // Update bow visual position (follow the hero).
        if (bowVisualInstance != null)
        {
            bowVisualInstance.transform.position = owner.transform.position
                                                 + owner.transform.forward * bowSpawnDistance
                                                 + Vector3.up * bowSpawnHeight;
            bowVisualInstance.transform.rotation = owner.transform.rotation * Quaternion.Euler(bowVisualRotationOffset);
        }

        // Tint the bow toward glowColor, and crank the emission so the glow is visible.
        // Past overchargeTime, switch tint and boost emission further.
        float chargeT = Mathf.Clamp01(chargeTime / fullChargeTime);
        Color target;
        float intensity;
        float lightT;
        if (chargeTime >= overchargeTime)
        {
            target = overchargeColor;
            intensity = emissionIntensity * 1.6f;
            lightT = 1.5f;
        }
        else
        {
            target = Color.Lerp(Color.white, glowColor, chargeT);
            intensity = emissionIntensity * chargeT;
            lightT = chargeT;
        }
        TintBow(target, intensity);

        if (glowLight != null)
        {
            glowLight.color = target;
            glowLight.intensity = maxLightIntensity * lightT;
            glowLight.range = Mathf.Max(0.5f, maxLightRange * lightT);
        }

        if (chargeTime >= overchargeTime && !overchargeReached)
        {
            overchargeReached = true;
            if (overchargeVfxPrefab != null && bowVisualInstance != null)
                overchargeVfxInstance = Instantiate(overchargeVfxPrefab, bowVisualInstance.transform.position, bowVisualInstance.transform.rotation, bowVisualInstance.transform);
        }

        return false;
    }

    public override bool OnFireUp(Hero owner)
    {
        if (!charging) return false;
        bool fired = false;

        if (chargeTime >= minReleaseTime)
        {
            if (chargeTime >= overchargeTime && deathBeamPrefab != null)
            {
                FireDeathBeam(owner);
            }
            else if (arrowPrefab != null)
            {
                FireArrow(owner);
            }
            fired = true;
        }

        EndCharge(owner);
        return fired;
    }

    // ---- Helpers ----

    private void StartCharge(Hero owner)
    {
        charging = true;
        chargeTime = 0f;
        overchargeReached = false;
        owner.speedMultiplier = slowdownWhileCharging;

        if (bowVisualPrefab != null)
        {
            Vector3 pos = owner.transform.position + owner.transform.forward * bowSpawnDistance + Vector3.up * bowSpawnHeight;
            bowVisualInstance = Instantiate(bowVisualPrefab, pos, owner.transform.rotation * Quaternion.Euler(bowVisualRotationOffset));
            // Disable any colliders on the bow visual so it can't shove the hero.
            foreach (var c in bowVisualInstance.GetComponentsInChildren<Collider>()) c.enabled = false;
            CacheBowMaterials();
            TintBow(Color.white, 0f);

            // Optional Point Light (works regardless of bow shader).
            if (useGlowLight)
            {
                GameObject lightGO = new GameObject("BowGlowLight");
                lightGO.transform.SetParent(bowVisualInstance.transform, false);
                lightGO.transform.localPosition = Vector3.zero;
                glowLight = lightGO.AddComponent<Light>();
                glowLight.type = LightType.Point;
                glowLight.color = glowColor;
                glowLight.intensity = 0f;
                glowLight.range = 0.5f;
                glowLight.shadows = LightShadows.None;
            }
        }
    }

    private void EndCharge(Hero owner)
    {
        charging = false;
        chargeTime = 0f;
        overchargeReached = false;
        owner.speedMultiplier = 1f;
        bowMats.Clear();
        glowLight = null; // destroyed with the bow visual since it's a child
        if (bowVisualInstance != null) Destroy(bowVisualInstance);
        if (overchargeVfxInstance != null) Destroy(overchargeVfxInstance);
    }

    private void FireArrow(Hero owner)
    {
        float chargeT = Mathf.Clamp01(chargeTime / fullChargeTime);
        float dmg = Mathf.Lerp(arrowMinDamage, arrowMaxDamage, chargeT);
        float scale = Mathf.Lerp(arrowMinScale, arrowMaxScale, chargeT);

        Vector3 spawn = owner.transform.position + owner.transform.forward * 0.8f + Vector3.up * bowSpawnHeight;
        Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
        p.transform.localScale = arrowPrefab.transform.localScale * scale;
        if (arrowSpeed > 0f) p.speed = arrowSpeed;
        p.Launch(owner.transform.forward, dmg, enemyLayers);
    }

    private void FireDeathBeam(Hero owner)
    {
        Vector3 spawn = owner.transform.position;
        DeathBeam beam = Instantiate(deathBeamPrefab, spawn, owner.transform.rotation);
        beam.Init(owner, enemyLayers);
    }

    // ---- Bow tinting (similar to DamageFlash) ----

    private void CacheBowMaterials()
    {
        bowMats.Clear();
        if (bowVisualInstance == null) return;
        foreach (Renderer r in bowVisualInstance.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                int prop = m.HasProperty(kBaseColor) ? kBaseColor : m.HasProperty(kColor) ? kColor : 0;
                if (prop == 0) continue;
                bowMats.Add(new MatRef { mat = m, prop = prop, original = m.GetColor(prop) });
            }
        }
    }

    private void TintBow(Color color, float emissionStrength)
    {
        for (int i = 0; i < bowMats.Count; i++)
        {
            // Tint the base color so it shifts toward the glow color.
            Color blended = Color.Lerp(bowMats[i].original, color, 0.85f);
            bowMats[i].mat.SetColor(bowMats[i].prop, blended);

            // Drive emission so the glow is visible regardless of base color.
            // Standard shader needs the _EMISSION keyword enabled AND the GI flag cleared
            // of EmissiveIsBlack (otherwise it short-circuits emission at render time
            // even when the keyword is on).
            if (bowMats[i].mat.HasProperty(kEmission))
            {
                if (emissionStrength > 0.001f) bowMats[i].mat.EnableKeyword("_EMISSION");
                else                           bowMats[i].mat.DisableKeyword("_EMISSION");
                bowMats[i].mat.SetColor(kEmission, color * emissionStrength);
                bowMats[i].mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
    }
}
