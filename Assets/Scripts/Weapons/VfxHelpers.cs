using UnityEngine;

/// <summary>
/// Shared helpers for taming runtime-spawned VFX prefabs from third-party
/// asset packs. Most packs ship prefabs that:
///   * Push enemies around via ParticleSystem.collision modules or
///     Rigidbody-driven sub-emitters
///   * Loop their particle systems and never self-terminate
///   * OR play a one-shot intro animation and then disappear, when the game
///     wants the visual to stay up for the duration of an effect
///
/// These helpers strip / reconfigure a prefab's runtime components after
/// instantiation so the visual matches the intent without needing to fork
/// the original prefab.
/// </summary>
public static class VfxHelpers
{
    /// <summary>
    /// Strip every component that could shove enemies, the hero, or the
    /// player off the spawn point. Targets all the usual suspects:
    ///   * Colliders (disabled rather than destroyed so the prefab's
    ///     transform hierarchy stays intact for the visuals)
    ///   * Rigidbodies (made kinematic + zero-velocity so any baked
    ///     forces don't push other physics bodies)
    ///   * ParticleSystem.collision modules (turned off so high-quality
    ///     particle collisions don't apply forces to nearby rigidbodies)
    /// Safe to call on any spawned VFX root — components missing on the
    /// prefab are simply skipped.
    /// </summary>
    public static void DisablePhysicsInterference(GameObject root)
    {
        if (root == null) return;

        foreach (var c in root.GetComponentsInChildren<Collider>(true))
            c.enabled = false;

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var col = ps.collision;
            col.enabled = false;
            // sendCollisionMessages off so OnParticleCollision callbacks
            // (which mob AI scripts sometimes subscribe to) don't fire
            // for cosmetic VFX particles either.
            col.sendCollisionMessages = false;
        }
    }

    /// <summary>
    /// Force every ParticleSystem in the hierarchy to use
    /// <see cref="ParticleSystemScalingMode.Hierarchy"/> so a uniform scale
    /// applied to the root transform actually shrinks / grows the particles.
    /// Many third-party packs ship with <c>scalingMode = Local</c> (only the
    /// system's own transform scale matters, parent ignored) or
    /// <c>scalingMode = Shape</c> (only the emission shape scales, particle
    /// sizes don't), which makes setting <c>localScale</c> on a wrapping
    /// host do nothing visible. Call this before adjusting localScale on
    /// the wrapper.
    /// </summary>
    public static void ForceHierarchyScaling(GameObject root)
    {
        if (root == null) return;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    /// <summary>
    /// Uniformly scale every NON-particle renderer's transform by
    /// <paramref name="scale"/>, while forcing every ParticleSystem to
    /// <see cref="ParticleSystemScalingMode.Local"/> so it stays at its
    /// authored size regardless of any parent-transform scale changes.
    ///
    /// Use this when you want the "static" parts of a VFX (ground crack
    /// decals, mesh-based shockwaves, sprite quads) to grow with an effect
    /// radius, but the particles (sparks, smoke, fire) to keep their
    /// original visual size — a 2× boosted grenade should leave a 2× big
    /// ground crack but the spark particles shouldn't suddenly look
    /// chunky.
    /// </summary>
    public static void ScaleStaticRenderersOnly(GameObject root, float scale)
    {
        if (root == null || scale <= 0.0001f) return;

        // First, lock every ParticleSystem into Local scaling. With Local
        // scaling, particles use ONLY their own transform's scale and
        // ignore parent transforms — so any local-scale we apply to a
        // mesh renderer's transform on the way down won't accidentally
        // resize particles parented underneath it.
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Local;
        }

        // Then scale every NON-particle renderer's transform. We collect
        // the unique parent transforms first so a single GameObject hosting
        // multiple non-particle renderers (rare but possible) only gets
        // scaled once.
        var seen = new System.Collections.Generic.HashSet<Transform>();
        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        {
            if (rend is ParticleSystemRenderer) continue;
            if (rend.transform == null) continue;
            if (!seen.Add(rend.transform)) continue;
            rend.transform.localScale = rend.transform.localScale * scale;
        }
    }

    /// <summary>
    /// Force every ParticleSystem in the hierarchy to NOT loop, so a prefab
    /// that ships with main.loop = true still plays its emission burst and
    /// then ends. Used for one-shot VFX (meteor impact, grenade detonation)
    /// where the explicit Destroy() schedule on the host should match the
    /// visual's natural finish instead of looping until destroyed.
    /// </summary>
    public static void ConfigureAsOneShotVfx(GameObject root)
    {
        if (root == null) return;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.None;
        }
        // If the prefab has an Animator with a looping clip, we leave it
        // alone — animator clips are a separate concern from particle loops
        // and most one-shot VFX don't have animators anyway.
    }

    /// <summary>
    /// Force every ParticleSystem in the hierarchy to loop indefinitely
    /// AND extends each system's duration to a very long value so cycle
    /// resets don't visibly punctuate the effect (a "shield reappearing
    /// every 2s" wave is the cycle restart, not the loop itself). Disables
    /// every Animator / Animation component so a play-once intro→outro
    /// clip can't pull the visual off-screen mid-effect, AND disables every
    /// user-script MonoBehaviour on the prefab — most VFX packs ship
    /// custom lifetime / scale-over-time / fade controllers that drive the
    /// animation independently of Animator and would re-trigger waves
    /// regardless of our particle loop config. Used for the I-am-Tank
    /// shield, which needs to stay visually static for 35s+.
    ///
    /// Disabling MonoBehaviours is heavy-handed but safe for spawned VFX:
    /// nothing on a third-party visual prefab is supposed to be running
    /// game logic; the only thing that should keep working is the
    /// ParticleSystem hierarchy itself, which is a built-in Unity Component
    /// (not a MonoBehaviour) and therefore unaffected.
    /// </summary>
    public static void ConfigureAsLoopingVfx(GameObject root)
    {
        if (root == null) return;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = true;
            // Extend the cycle so the system effectively never resets.
            // Many shield prefabs have a 2-3s main.duration which causes a
            // visible "wave" every duration when looping is on; pushing
            // duration to ~hours of game time keeps the system in cycle 1
            // forever from the player's POV.
            if (main.duration < 9999f) main.duration = 9999f;
            main.stopAction = ParticleSystemStopAction.None;
            if (!ps.isPlaying) ps.Play(true);
        }
        foreach (var anim in root.GetComponentsInChildren<Animator>(true))
            anim.enabled = false;
        foreach (var anim in root.GetComponentsInChildren<Animation>(true))
            anim.enabled = false;
        // Disable every user script (MonoBehaviour) attached to the prefab
        // — these are typically VFX controller helpers ("CFX_AutoStop",
        // "ScaleOverTime", "DestroyAfterSeconds", "PingPongScale") that
        // would override our loop config and walk the visual through its
        // intended one-shot lifecycle. Disabling them is purely cosmetic;
        // the ParticleSystem + Renderer pipeline keeps running fine
        // because those are Unity built-ins, not MonoBehaviours.
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            mb.enabled = false;
    }

    /// <summary>
    /// Simulate every ParticleSystem in the hierarchy forward to
    /// <paramref name="simulationTime"/> seconds and then pause it, freezing
    /// the entire visual at that moment in its lifecycle. This is the
    /// nuclear option for prefabs whose built-in ParticleSystem config
    /// itself cycles (bursts at fixed times within the cycle, sub-emitters
    /// triggered on cycle end, particle lifetimes that produce visible
    /// "waves") — once paused, the simulation doesn't advance, no new
    /// particles are emitted, and the existing particles hang in place at
    /// whatever pose they had at <paramref name="simulationTime"/>.
    ///
    /// Tune <paramref name="simulationTime"/> in the inspector until the
    /// freeze captures the prefab in its "fully formed" state — for a
    /// shield, somewhere ~1-2s into the prefab's normal play tends to land
    /// past the intro burst and onto the steady shield silhouette. Too
    /// early and you get the assembling animation frozen; too late and the
    /// outro / fade-out has started.
    ///
    /// Also disables Animators, Animation, and user MonoBehaviour scripts
    /// so nothing else can advance the visual past the frozen frame.
    /// </summary>
    public static void FreezeVfxAtTime(GameObject root, float simulationTime)
    {
        if (root == null) return;
        float simT = Mathf.Max(0f, simulationTime);
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            // Simulate(restart=true, withChildren=false) advances JUST this
            // system to simT; we walk children explicitly via the outer
            // GetComponentsInChildren loop so each sub-emitter gets a clean
            // simulate-from-zero pass. The third arg (fixedTimeStep=true)
            // uses a deterministic step which is the safest for repeatable
            // freeze frames across runs.
            ps.Simulate(simT, false, true);
            ps.Pause();
        }
        foreach (var anim in root.GetComponentsInChildren<Animator>(true))
            anim.enabled = false;
        foreach (var anim in root.GetComponentsInChildren<Animation>(true))
            anim.enabled = false;
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            mb.enabled = false;
    }
}
