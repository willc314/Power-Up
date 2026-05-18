using UnityEngine;

/// <summary>
/// One ghostly elemental dagger spawned by the Dagger's Elemental Shiv
/// augment when the player hits an enemy. The clone spawns at a random
/// angle around the target, flies in a straight line that passes THROUGH
/// the target, and applies its damage + slow/damage debuff at the moment
/// it reaches the target's position. Self-destroys at the end of its
/// short flight regardless of whether it hit anything.
///
/// Visual is a translucent unlit-color render of the user-supplied clone
/// prefab so it reads as a "ghost" copy of the dagger rather than a solid
/// model. The translucency / hue / unlit treatment is applied at runtime
/// via material override so the user can drop in any model.
/// </summary>
public class ElementalShivClone : MonoBehaviour
{
    private Enemy target;
    private Vector3 startPos;
    private Vector3 endPos;
    private float duration;
    private float damage;
    private float debuffDuration;
    private float debuffSlowFactor;
    private float debuffDamageFactor;

    private float timer;
    private bool damageApplied;

    /// <summary>
    /// Fire-and-forget clone spawn. <paramref name="target"/> is the enemy
    /// that was hit by the parent dagger strike — the clone aims its damage
    /// + debuff specifically at this enemy (no AOE, no incidental hits on
    /// other enemies in the path), so the augment reads as "X copies all
    /// striking the same target" instead of a small AOE.
    ///
    /// The clone's start / end positions are computed from
    /// <paramref name="approachAngleDeg"/> in the horizontal plane: it
    /// spawns at <c>spawnDistance</c> away from the target along that
    /// angle, and travels to the same distance on the OPPOSITE side, so
    /// it passes straight through.
    /// </summary>
    public static ElementalShivClone Spawn(
        Enemy target,
        float approachAngleDeg,
        float spawnDistance,
        float travelDuration,
        float damage,
        float debuffDuration,
        float debuffSlowFactor,
        float debuffDamageFactor,
        GameObject visualPrefab,
        Vector3 visualRotationOffset,
        Color tintColor,
        float verticalOffset)
    {
        if (target == null) return null;

        var go = new GameObject("ElementalShivClone");
        var c = go.AddComponent<ElementalShivClone>();
        c.target             = target;
        c.duration           = Mathf.Max(0.05f, travelDuration);
        c.damage             = Mathf.Max(0f, damage);
        c.debuffDuration     = debuffDuration;
        c.debuffSlowFactor   = debuffSlowFactor;
        c.debuffDamageFactor = debuffDamageFactor;

        // Compute approach vector in the XZ plane and offset the start +
        // end positions so the clone passes directly through the target's
        // position at t=0.5.
        float rad = approachAngleDeg * Mathf.Deg2Rad;
        Vector3 approachDir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        Vector3 targetCenter = target.transform.position + Vector3.up * verticalOffset;
        c.startPos = targetCenter - approachDir * spawnDistance;
        c.endPos   = targetCenter + approachDir * spawnDistance;

        go.transform.position = c.startPos;
        // Face along the travel direction so the model points the right
        // way as it flies.
        go.transform.rotation = Quaternion.LookRotation(approachDir, Vector3.up);

        if (visualPrefab != null)
        {
            var vfx = Instantiate(visualPrefab, go.transform);
            vfx.transform.localPosition = Vector3.zero;
            // Apply the user-tuned rotation offset to align the model's
            // "blade direction" with the host transform's forward (which
            // points along the travel vector). Without this, models that
            // point along their local +X or +Y instead of +Z fly
            // "sideways" relative to their motion.
            vfx.transform.localRotation = Quaternion.Euler(visualRotationOffset);
            // Prevent any baked physics from interfering with the target.
            VfxHelpers.DisablePhysicsInterference(vfx);
            // Defensively disable every user script on the visual prefab
            // — most users drop in a raw model, but if they accidentally
            // drop in (say) the original thrown-dagger prefab, the live
            // Projectile script attached would tick away with no Launch()
            // setup. Disabling all MonoBehaviours here keeps the visual
            // purely cosmetic regardless of what was on the prefab.
            // ParticleSystem / Renderer / MeshFilter aren't MonoBehaviours
            // so they keep working fine.
            foreach (var mb in vfx.GetComponentsInChildren<MonoBehaviour>(true))
                mb.enabled = false;
            // Animator / Animation inherit from Behaviour (NOT MonoBehaviour)
            // so the loop above misses them. Many FBX imports auto-generate
            // an Animator on the prefab root which keeps driving the
            // transform every frame, overwriting whatever localRotation we
            // just set — that's why setting the rotation offset on the
            // weapon appeared to do nothing. Disabling them here lets our
            // local-rotation override actually stick.
            foreach (var anim in vfx.GetComponentsInChildren<Animator>(true))
                anim.enabled = false;
            foreach (var anim in vfx.GetComponentsInChildren<Animation>(true))
                anim.enabled = false;
            // Translucent unlit override so the clone reads as a "ghost"
            // copy — pure tinted color with the configured low alpha,
            // independent of the source prefab's authored materials.
            ApplyGhostMaterial(vfx, tintColor);
        }

        return c;
    }

    /// <summary>
    /// Walk every Renderer in the spawned visual and replace its material
    /// with a runtime unlit-transparent material tinted to
    /// <paramref name="tint"/>. Falls back through a list of common
    /// shaders (BIRP-friendly first) so this works on any project that
    /// has the standard Unity shader set installed.
    /// </summary>
    private static void ApplyGhostMaterial(GameObject root, Color tint)
    {
        Shader unlitShader = Shader.Find("Particles/Standard Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Transparent");
        if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
        if (unlitShader == null) unlitShader = Shader.Find("Standard");

        var ghostMat = new Material(unlitShader);
        ghostMat.color = tint;
        // Standard / Particles Standard Unlit need their rendering mode
        // set explicitly for transparency to work as expected.
        if (ghostMat.HasProperty("_Mode")) ghostMat.SetFloat("_Mode", 3f);              // 3 = Transparent on the Standard shader
        if (ghostMat.HasProperty("_Surface")) ghostMat.SetFloat("_Surface", 1f);        // URP fallback (1 = Transparent)
        if (ghostMat.HasProperty("_Color"))   ghostMat.SetColor("_Color", tint);
        if (ghostMat.HasProperty("_BaseColor")) ghostMat.SetColor("_BaseColor", tint);
        if (ghostMat.HasProperty("_TintColor")) ghostMat.SetColor("_TintColor", tint);
        ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ghostMat.SetInt("_ZWrite", 0);
        ghostMat.DisableKeyword("_ALPHATEST_ON");
        ghostMat.EnableKeyword("_ALPHABLEND_ON");
        ghostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        ghostMat.renderQueue = 3000;

        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        {
            // Drop ALL existing materials so even multi-material prefabs
            // come out fully ghosted.
            int count = rend.sharedMaterials.Length;
            var mats = new Material[count];
            for (int i = 0; i < count; i++) mats[i] = ghostMat;
            rend.sharedMaterials = mats;
        }
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);
        transform.position = Vector3.Lerp(startPos, endPos, t);

        // Apply damage + debuff at the midpoint — that's the moment the
        // clone is visually overlapping the target. Single-shot per clone
        // (one damage pulse per ghost), gated by damageApplied so nothing
        // double-fires if Update happens to run twice in one cycle.
        if (!damageApplied && t >= 0.5f)
        {
            damageApplied = true;
            if (target != null && !target.IsDead)
            {
                target.TakeDamage(damage);
                target.ApplyShivDebuff(debuffDuration, debuffSlowFactor, debuffDamageFactor);
            }
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
