using System.Collections;
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
    // The bow used to drive a per-charge material tint (glowColor /
    // emissionIntensity) and an attached point light (useGlowLight /
    // maxLightIntensity / maxLightRange) to telegraph the charge state. Those
    // fields and the accompanying TintBow / CacheBowMaterials machinery were
    // removed when the hero-aura system took over the feedback role — the
    // Lightning aura at full charge and the Plexus aura at overcharge are
    // visually clearer and don't depend on bow shader/material support.

    [Header("Charge")]
    [Tooltip("Seconds to reach full normal-shot charge. After this, damage and arrow size are at max.")]
    public float fullChargeTime = 1.5f;
    [Tooltip("Hero speed multiplier while charging (0 = stuck, 1 = no slowdown).")]
    [Range(0f, 1f)] public float slowdownWhileCharging = 0.4f;
    [Tooltip("Minimum charge time required to actually fire something. Below this, the release does nothing (prevents accidental dry-fires).")]
    public float minReleaseTime = 0.1f;
    [Tooltip("Fraction of incoming damage absorbed while the hero is charging this bow. 0.30 = the hero takes 70% damage while drawing. Read by Hero.TakeDamage and applied before HP subtraction. The slowdown means the player is stuck in place with limited mobility — this reduction makes that risk worthwhile.")]
    [Range(0f, 1f)] public float chargingDamageReduction = 0.30f;

    [Header("Arrow")]
    [Tooltip("Prefab spawned on release for normal shots. Should have a Projectile component (use Arrow_Piercing.prefab).")]
    public Projectile arrowPrefab;
    [Tooltip("If > 0, overrides the projectile prefab's Speed so bow arrows can fly faster than crossbow arrows. Set to 0 to use the prefab's default.")]
    public float arrowSpeed = 45f;
    [Tooltip("Base lifetime (seconds) of every spawned bow arrow before any Range boost. Overrides the prefab's lifetime. Range boosts (Grenade pickups) add arrowLifetimeBonus on top of this — a maxed Range boost takes a 2s arrow up to 6s.")]
    public float arrowBaseLifetime = 2f;
    [Tooltip("How far in front of the hero the arrow spawns. Keep small (e.g. 0.2) so enemies hugging the hero still get hit — large values can spawn the arrow past the target.")]
    public float arrowSpawnForward = 0.2f;
    [Tooltip("Damage at zero charge.")]
    public float arrowMinDamage = 10f;
    [Tooltip("Damage at full charge (and beyond).")]
    public float arrowMaxDamage = 70f;
    [Tooltip("Visual scale multiplier at zero charge.")]
    public float arrowMinScale = 1f;
    [Tooltip("Visual scale multiplier at full charge.")]
    public float arrowMaxScale = 2.5f;

    [Header("Arrow Damage Falloff")]
    [Tooltip("Multiplier applied to the running damage scale after every successful hit. 0.5 = damage halves with each subsequent hit. 1 = no falloff. Combined with arrowDamageFalloffFloor so chained / loop-back hits never drop below a useful floor.")]
    [Range(0f, 1f)] public float arrowDamageFalloffPerHit = 0.5f;
    [Tooltip("Minimum damage multiplier the arrow can fall to. 0.25 = each hit caps at 25% of the original damage no matter how many enemies the arrow has already chained through. Set to 0 for unbounded decay or 1 to disable falloff entirely.")]
    [Range(0f, 1f)] public float arrowDamageFalloffFloor = 0.25f;

    [Header("Death Beam (Overcharge)")]
    [Tooltip("Holding past this many seconds switches the release to a Death Beam.")]
    public float overchargeTime = 5f;
    [Tooltip("Prefab spawned on overcharged release. Should have a DeathBeam component on a stretched cube/cylinder.")]
    public DeathBeam deathBeamPrefab;

    [Header("Audio / VFX (optional)")]
    [Tooltip("Optional particle prefab spawned on the bow when entering overcharge.")]
    public GameObject overchargeVfxPrefab;

    [Header("Hero Aura VFX (optional)")]
    [Tooltip("Spawned on the HERO (not the bow) the moment the bow reaches full charge. Stays on for the rest of the charge until release/interrupt. Designed for Hovl Studio's 'Lightning aura' or any character-aura-style prefab.")]
    public GameObject fullyChargedHeroAuraPrefab;

    [Tooltip("Spawned on the HERO (not the bow) the moment the bow enters overcharge. Stacks ON TOP of the Lightning aura — the fully-charged aura stays alive too. Designed for Hovl Studio's 'Plexus' prefab or any heavier aura you want layered on top.")]
    public GameObject overchargedHeroAuraPrefab;

    [Tooltip("Vertical offset above the hero's pivot where the aura prefabs spawn. Most character-aura prefabs are authored centered on the body, so 0–1 reads well. Tweak if the aura sits at the feet or above the head.")]
    public float heroAuraYOffset = 0f;

    [Tooltip("Uniform scale applied to spawned hero aura prefabs. Useful when the source pack was authored at a different character size than the hero.")]
    public float heroAuraScale = 1f;

    [Header("Heavenly Gale (level-up augment)")]
    [Tooltip("True after the player accepts the Heavenly Gale augment. Replaces the bow's normal release with a barrage of homing arrows in oscillating angles, with a chance to call down a death beam from the sky on each shot. Both charged + overcharged auras spawn together at full charge for the buffed visual feedback.")]
    public bool heavenlyGaleEnabled = false;
    [Tooltip("Fixed charge time required to fire the Gale barrage. AttackSpeed pickups DON'T reduce this — that boost slot is repurposed to the in-barrage fire rate while Heavenly Gale is active.")]
    public float galeChargeTime = 3f;
    [Tooltip("Base barrage duration in seconds. Range pickups (Grenade) extend this.")]
    public float galeBarrageBaseDuration = 2.5f;
    [Tooltip("Extra barrage seconds per Range pickup.")]
    public float galeBarrageDurationPerBoost = 0.4f;
    [Tooltip("Cap on barrage duration. Default 8s = up to ~6 extra seconds on top of base.")]
    public float galeBarrageMaxDuration = 8f;
    [Tooltip("Base interval (seconds) between successive barrage shots.")]
    public float galeBarrageBaseInterval = 0.18f;
    [Tooltip("Multiplicative reduction per AttackSpeed pickup. 0.85 = each pickup makes the interval 85% of its previous value (faster fire).")]
    [Range(0.5f, 1f)] public float galeBarrageIntervalPerBoost = 0.85f;
    [Tooltip("Floor on the barrage interval (seconds). Even with infinite AttackSpeed pickups it can't go below this.")]
    public float galeBarrageMinInterval = 0.05f;
    [Tooltip("Base arrows fired per barrage shot.")]
    public int galeBarrageBaseProjectilesPerShot = 1;
    [Tooltip("Extra arrows per shot per Projectiles (Crossbow) pickup.")]
    public int galeBarrageProjectilesPerBoost = 1;
    [Tooltip("Cap on arrows-per-shot during the barrage.")]
    public int galeBarrageMaxProjectilesPerShot = 6;
    [Tooltip("Damage angle envelope per side, in degrees. Arrows oscillate between -this and +this around the hero's forward.")]
    public float galeOscillationDegreesPerSide = 50f;
    [Tooltip("Step size in degrees for the angle oscillation. Each shot advances the angle by this; the angle ping-pongs at the envelope boundaries.")]
    public float galeOscillationStepDegrees = 10f;
    [Tooltip("Probability per FIRED ARROW (not per shot) to also call down a sky death beam on a random alive enemy in the area. 0.05 = 5% chance per arrow. Independent per arrow.")]
    [Range(0f, 1f)] public float galeDeathBeamChancePerArrow = 0.05f;
    [Tooltip("Duration (seconds) of each Heavenly Gale sky death beam. Per spec — 2.5s.")]
    public float galeDeathBeamDuration = 2.5f;
    [Tooltip("Visual radius multiplier for the Heavenly Gale sky beam — the prefab's radius times this. <1 = thin beam (per spec — relatively thin).")]
    [Range(0.05f, 1f)] public float galeDeathBeamRadiusMultiplier = 0.35f;
    [Tooltip("Beam length (also the distance from the target to the beam's top end). The beam's bottom end sits at the target enemy and the beam extends this many units along the sky direction.")]
    public float galeDeathBeamSkyHeight = 30f;
    [Tooltip("Minimum tilt (in degrees) from straight-down for each sky beam. Setting this above 0 guarantees no beams ever come PERFECTLY vertical, so consecutive casts visually feel different from each other rather than stacking on the same axis.")]
    [Range(0f, 60f)] public float galeDeathBeamMinTiltDegrees = 12f;
    [Tooltip("Maximum tilt (in degrees) from straight-down for each sky beam. Each beam picks a random tilt in [min..max] in a random horizontal direction. Higher values = more dramatic angle variety; 50-60° feels like dramatic incoming strikes, 20-30° feels like minor variation.")]
    [Range(0f, 60f)] public float galeDeathBeamMaxTiltDegrees = 50f;
    [Tooltip("Distance the sky beam visually extends BEYOND the target (in the beam's forward direction, i.e. into / through the ground). Lets the beam read as a full beam striking the ground rather than ending exactly at the target's feet. Tune to ~half the visual ground thickness.")]
    public float galeDeathBeamGroundExtension = 4f;
    [Tooltip("Multiplier on the sky beam's emission color for bloom intensity. Values > 1 push the emission into HDR territory so the post-process bloom kicks in. Tint color (hue) is unchanged — only the emission gets boosted.")]
    public float galeDeathBeamBloomIntensity = 4f;
    [Tooltip("Camera shake amplitude per Heavenly Gale sky beam. Overrides the prefab's much-stronger default — sky beams fire frequently during a barrage and the prefab's full shake stacks into screen-shaking-violently territory. Keep low (0.05-0.10) so the audio + visuals carry the impact instead of the camera.")]
    [Range(0f, 1f)] public float galeDeathBeamShakeAmplitude = 0.05f;
    [Tooltip("Search radius around the hero when picking a target enemy for a sky beam. If no enemy is in range, the beam is suppressed (no fallback random spot).")]
    public float galeDeathBeamTargetSearchRadius = 25f;

    [Tooltip("Pierce added per BOW pickup while Heavenly Gale is active. Bow / Sword / Shield pickups all map to BoostKind.Damage, so this exists to give the bow pickup specifically a distinct effect — pierce stacks on every barrage arrow via the existing arrowPierceBonus path.")]
    public int galeBowPickupPierceBonus = 1;
    [Tooltip("Maximum total pierce on a Heavenly Gale barrage arrow. Clamps the sum of fullyChargedPierceCount + arrowPierceBonus AFTER all bonuses have been applied. Keeps a single arrow from chaining through every enemy on the screen even if the player has stacked many bow pickups. Set to a large value to effectively disable the cap.")]
    public int galeBarrageMaxPierce = 3;
    [Tooltip("Multiplier on the standard Damage boost when a BOW pickup is applied under Heavenly Gale. 0.5 = the bow pickup gives HALF a regular damage boost on top of its pierce gain. Keeps the bow pickup a meaningful bump without overshadowing sword / shield damage pickups.")]
    [Range(0f, 2f)] public float galeBowPickupDamageScale = 0.5f;
    [Tooltip("Arrow lifetime bonus added per SHIELD pickup while Heavenly Gale is active. Stacks on arrowLifetimeBonus, which FireGaleBarrageShot reads at spawn time so each barrage arrow flies longer.")]
    public float galeShieldPickupLifetimeBonus = 0.5f;
    [Tooltip("Multiplier on the standard Damage boost when a SHIELD pickup is applied under Heavenly Gale. 1.0 = full damage boost on top of the lifetime extension; tune below 1 if shield should be lifetime-focused.")]
    [Range(0f, 2f)] public float galeShieldPickupDamageScale = 1f;

    /// <summary>
    /// Transient hint set by <see cref="PowerUpChoiceUI"/> immediately before
    /// invoking the boost button, telling this bow which weapon-type pickup
    /// the player is consuming. Lets Heavenly Gale distinguish a bow pickup
    /// from a sword / shield pickup (all three map to BoostKind.Damage), so
    /// only the bow pickup grants the pierce + minor-damage variant. Reset
    /// to <c>none</c> after the boost is consumed.
    /// </summary>
    [System.NonSerialized] public eWeaponType pickupSourceTypeHint = eWeaponType.none;

    [Header("Heavenly Gale — Barrage Arrow Visual")]
    [Tooltip("Custom Projectile prefab spawned per arrow during the Heavenly Gale barrage (separate from the regular arrowPrefab). Should have a Tiny.Trail child for the rainbow trail. Falls back to arrowPrefab when null.")]
    public Projectile galeBarrageArrowPrefab;
    [Tooltip("Alpha channel applied to the random rainbow tint on the barrage arrow's trail. Low values (~0.2 = 50/255) keep the trail translucent so multiple overlapping trails read as a single ghostly streak instead of a solid blob.")]
    [Range(0f, 1f)] public float galeArrowTrailAlpha = 0.196f;

    // ---- Heavenly Gale runtime state ----
    // (Per-cast timer / oscillation state lives in HeavenlyGaleBarrageCoroutine
    // as locals — no class fields needed for them.)
    private bool galeBarrageActive;
    private float galeBarrageDuration;                 // resolved per-cast
    private float galeBarrageInterval;                 // resolved per-cast
    private int   galeBarrageProjectilesPerShot;      // resolved per-cast

    // Per-boost-type counters used by Heavenly Gale's "reapply old powerups
    // under the new pipeline" pass at augment-acceptance time. Kept up to
    // date by TryApplyBoost regardless of whether HG is active so the
    // augment can recompute correctly when picked.
    private int rangeBoostsTaken;

    // Snapshots of the bow's "from-fresh" stat values, captured at Awake.
    // Heavenly Gale resets the bow to these before re-applying boosts so
    // the post-augment state matches what the player would have if they'd
    // had Heavenly Gale active from the start.
    private float origFullChargeTime;
    private float origOverchargeTime;
    private float origArrowMinDamage;
    private float origArrowMaxDamage;
    private float origPostOverchargeDamageRate;
    private bool  bowOriginalsCaptured;

    // ---- Runtime state ----
    private bool charging;
    private float chargeTime;
    private GameObject bowVisualInstance;
    private GameObject overchargeVfxInstance;
    private GameObject fullyChargedHeroAuraInstance;
    private GameObject overchargedHeroAuraInstance;
    private bool overchargeReached;
    private bool fullyChargedAuraReached;
    /// <summary>Damage multiplier accumulated while holding past overchargeTime. Applied to beam damage at fire and reset on EndCharge.</summary>
    private float postOverchargeAccumulated;
    /// <summary>Reference to the most recently fired death beam. While non-null (the GameObject is still alive), OnFireDown refuses to start a new charge — the player must wait for the beam to finish.</summary>
    private DeathBeam activeBeam;

    /// <summary>True while the player is actively drawing the bow. Read by Hero.TakeDamage to apply the charging damage reduction.</summary>
    public bool IsCharging => charging;
    /// <summary>True while a previously-fired death beam is still alive in the world. Read by OnFireDown to lock out new charges.</summary>
    public bool IsBeamActive => activeBeam != null;

    public override bool CanFire => true; // charging weapon — always allowed to start

    /// <summary>
    /// True when the assigned death-beam prefab uses the "1% of max HP per
    /// second" damage model. Powerup descriptions hide the irrelevant
    /// "+Beam DPS" string when this is true so the boost label stays honest.
    /// </summary>
    private bool BeamUsesMaxHpDamage =>
        deathBeamPrefab != null && deathBeamPrefab.maxHpFractionPerSecond > 0f;

    // ---- Boost overrides ----
    [Header("Boost Tuning — Arrow Damage")]
    [Tooltip("How much arrowMaxDamage grows per Damage boost.")]
    public float maxDamageIncreasePerLevel = 10f;
    [Tooltip("How much arrowMinDamage also grows per Damage boost.")]
    public float minDamageIncreasePerLevel = 3f;

    [Header("Boost Tuning — Charge Speed")]
    [Tooltip("Floor for fullChargeTime when applying AttackSpeed boosts.")]
    public float minFullChargeTime = 0.4f;
    [Tooltip("Charge speed gained per AttackSpeed boost (fraction). 0.15 = +15% charge speed per pickup. fullChargeTime shrinks accordingly with diminishing returns as it approaches minFullChargeTime.")]
    [Range(0f, 1f)] public float chargeSpeedIncreasePercent = 0.15f;
    [Tooltip("Floor for overchargeTime (when the death beam triggers).")]
    public float minOverchargeTime = 1.5f;
    [Tooltip("Reduction in overchargeTime per AttackSpeed boost (fraction). 0.15 = -15% time-to-beam per pickup, diminishing as it approaches minOverchargeTime.")]
    [Range(0f, 1f)] public float overchargeSpeedIncreasePercent = 0.15f;

    [Header("Fully-Charged Homing")]
    [Tooltip("Charge fraction (0..1) at which arrows gain homing. 1.0 = only fully-charged shots home, 0.95 = slightly-under-full also homes (helps when frame timing nips a few ms off the perceived full charge).")]
    [Range(0.5f, 1f)] public float homingChargeThreshold = 0.95f;
    [Tooltip("Seconds a fully-charged arrow actively homes for, BEFORE the Range boost extends it. The Range boost adds arrowLifetimeBonus to this number too so the homing window grows alongside the arrow's lifespan. Default 2 matches the default arrow base lifetime so a fresh shot homes for its entire life.")]
    public float fullyChargedHomingDuration = 2f;
    [Tooltip("Base degrees/sec the fully-charged arrow can turn at the moment it last hit something. 360 = ~perfect aim (a full U-turn per second). Effective rate ramps higher while the arrow goes without landing a hit (see fullyChargedHomingTurnSpeedRamp).")]
    public float fullyChargedHomingTurnSpeed = 360f;
    [Tooltip("Degrees/sec ADDED to the arrow's effective turn rate per second it spends without landing a hit. Stops the arrow from spiraling around an enemy it can't quite catch — given a couple seconds the turn rate gets tight enough to break the orbit. Reset to zero on every hit. 360 means after 1 unhit second the arrow can U-turn in 0.5s.")]
    public float fullyChargedHomingTurnSpeedRamp = 360f;
    [Tooltip("Hard cap on the ramped turn rate (deg/sec). 0 or negative = uncapped; 2160 caps at six full rotations per second.")]
    public float fullyChargedHomingTurnSpeedMax = 2160f;
    [Tooltip("Range within which the fully-charged arrow looks for targets. Outside this it flies straight.")]
    public float fullyChargedHomingMaxRange = 40f;
    [Tooltip("Pierce count applied to fully-charged arrows so they keep chaining through enemies indefinitely. -1 = infinite pierce (recommended); a positive number caps the chain length.")]
    public int fullyChargedPierceCount = -1;

    [Header("Boost Tuning — Multi-Arrow (Projectiles boost)")]
    [Tooltip("How many arrows the normal attack fires per release. Crossbow pickups bump this by 1 every other upgrade — half the rate the Crossbow itself gets, since the bow's fully-charged shots already chain via homing.")]
    public int arrowProjectileCount = 1;
    [Tooltip("Cap on arrowProjectileCount.")]
    public int maxArrowProjectileCount = 5;
    [Tooltip("Total spread (degrees) for the fan when arrowProjectileCount > 1.")]
    public float arrowSpreadAngle = 25f;
    [Tooltip("Running count of Projectiles (Crossbow) pickups taken on this Bow. Used to gate +1 Arrow to every OTHER upgrade — odd-numbered pickups (1st, 3rd, 5th, …) grant an extra arrow; even-numbered ones still apply the rest of the boost (damage, beam duration, beam DPS) but skip the projectile bump.")]
    public int crossbowPickupCount = 0;

    [Header("Post-Overcharge Damage Rate")]
    [Tooltip("Once the player has held past overchargeTime, every additional second of holding adds this fraction to the death beam's damage multiplier. 0.20 = +20% beam damage per second held. Stack indefinitely if the player wants to commit to a giant nuke.")]
    public float postOverchargeDamageRate = 0.20f;
    [Tooltip("Cap on the post-overcharge damage multiplier (additive). 0 or negative = uncapped. 5 = up to +500% beam damage.")]
    public float maxPostOverchargeMultiplier = 0f;

    [Header("Boost Tuning — Death Beam")]
    [Tooltip("Total bonus DPS added to the spawned death beam from Damage boosts. Set by TryApplyBoost(Damage).")]
    public float deathBeamDpsBonus = 0f;
    [Tooltip("How much beam DPS grows per Damage boost.")]
    public float deathBeamDpsPerBoost = 25f;
    [Tooltip("Bonus radius added to the spawned death beam. Range boosts no longer touch this field — the bow's Range upgrade now boosts the beam's MaxHP%-per-second damage instead. Left in the inspector so it can still be set manually if desired.")]
    public float deathBeamRadiusBonus = 0f;
    [Tooltip("Legacy: used to be how much beam radius grew per Range boost. The Range boost no longer touches the radius (it now bumps deathBeamMaxHpPercentBonus instead), so this value is unused at runtime.")]
    public float deathBeamRadiusPerBoost = 0f;
    [Tooltip("Legacy cap on deathBeamRadiusBonus. Unused now that the Range boost no longer grows the radius.")]
    public float maxDeathBeamRadiusBonus = 0f;
    [Tooltip("Bonus PERCENT added to the death beam's max-HP-per-second damage. 1 = +1%/s of each enemy's max HP. Stacks ON TOP of the prefab's base maxHpFractionPerSecond. Set by TryApplyBoost(Range).")]
    public float deathBeamMaxHpPercentBonus = 0f;
    [Tooltip("Percent points added to deathBeamMaxHpPercentBonus per Range boost. 0.1 = +0.1% Max HP damage per pickup.")]
    public float deathBeamMaxHpPercentBonusPerBoost = 0.1f;
    [Tooltip("Cap on deathBeamMaxHpPercentBonus. Default 2 = up to +2% Max HP/s on top of the prefab's base 1%, so a fully-upgraded Range bow's beam ticks 3% Max HP/s.")]
    public float maxDeathBeamMaxHpPercentBonus = 2f;
    [Tooltip("Total bonus seconds added to each spawned arrow's lifetime from Range boosts (Grenade pickups). Lets fully-charged homing arrows stay alive long enough to chain through more enemies. Set by TryApplyBoost(Range).")]
    public float arrowLifetimeBonus = 0f;
    [Tooltip("How many extra seconds of arrow lifetime each Range boost adds.")]
    public float arrowLifetimeBonusPerBoost = 0.5f;
    [Tooltip("Cap on arrowLifetimeBonus (seconds). Default 4 = up to four extra seconds of homing on top of the prefab's base lifetime.")]
    public float maxArrowLifetimeBonus = 4f;
    [Tooltip("Total bonus pierce count added to each spawned arrow from Range boosts. Stacks ON TOP of the prefab's pierceCount for non-fully-charged shots. Fully-charged shots already pierce infinitely, so this is ignored on them. Set by TryApplyBoost(Range).")]
    public int arrowPierceBonus = 0;
    [Tooltip("How many extra pierces each Range boost adds.")]
    public int arrowPierceBonusPerBoost = 1;
    [Tooltip("Cap on arrowPierceBonus.")]
    public int maxArrowPierceBonus = 8;
    [Tooltip("Total bonus seconds added to the spawned death beam's duration. Bumped by Projectiles boosts (Crossbow pickup) so the beam stays out longer alongside the extra-arrow effect.")]
    public float deathBeamDurationBonus = 0f;
    [Tooltip("How many extra seconds of beam duration each Projectiles boost adds.")]
    public float deathBeamDurationPerBoost = 0.5f;
    [Tooltip("Cap on deathBeamDurationBonus.")]
    public float maxDeathBeamDurationBonus = 4f;

    private void Awake()
    {
        // Snapshot the bow's "from-fresh" stat values so the Heavenly Gale
        // augment can reset them and re-apply boosts under the new pipeline.
        // Awake runs before Start (the base-class hook that captures
        // OriginalDamage / OriginalCooldown), so subclass tweaks land here.
        if (!bowOriginalsCaptured)
        {
            bowOriginalsCaptured = true;
            origFullChargeTime          = fullChargeTime;
            origOverchargeTime          = overchargeTime;
            origArrowMinDamage          = arrowMinDamage;
            origArrowMaxDamage          = arrowMaxDamage;
            origPostOverchargeDamageRate = postOverchargeDamageRate;
        }
    }

    public override bool IsBoostMaxed(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                // Maxed only when BOTH charge and overcharge times have hit their floors.
                bool chargeFloor = fullChargeTime  <= minFullChargeTime + 0.001f;
                bool overFloor   = overchargeTime  <= minOverchargeTime + 0.001f;
                return chargeFloor && overFloor;

            case BoostKind.Range:
                // Range maxes only when ALL THREE pieces — beam Max HP%,
                // arrow lifetime, and arrow pierce — have hit their caps.
                return deathBeamMaxHpPercentBonus >= maxDeathBeamMaxHpPercentBonus - 0.001f
                    && arrowLifetimeBonus         >= maxArrowLifetimeBonus         - 0.001f
                    && arrowPierceBonus           >= maxArrowPierceBonus;

            case BoostKind.Damage:
                return IsDamageMaxed;

            case BoostKind.Projectiles:
                // Maxed only when arrow count, damage level, AND beam
                // duration are ALL at their caps. Any one of them having
                // room means the boost still has somewhere meaningful to land.
                return arrowProjectileCount >= maxArrowProjectileCount
                    && IsDamageMaxed
                    && deathBeamDurationBonus >= maxDeathBeamDurationBonus - 0.001f;
        }
        return base.IsBoostMaxed(kind);
    }

    public override string DescribeBoost(BoostKind kind)
    {
        // Heavenly Gale labels — the boost effects are remapped, so the
        // standard descriptions ("+1 Arrow", "Charge maxed") would mislead.
        if (heavenlyGaleEnabled && kind == BoostKind.Damage
            && pickupSourceTypeHint == eWeaponType.bow)
        {
            // Bow pickup specifically — pierce + minor damage. Show both
            // pieces unless pierce is at cap, in which case fall back to
            // the regular damage label so the player still sees what
            // they're getting.
            bool pierceFull = arrowPierceBonus >= maxArrowPierceBonus;
            float dmgPart = maxDamageIncreasePerLevel * Mathf.Max(0f, galeBowPickupDamageScale);
            if (pierceFull)
                return $"+{dmgPart:0.#} Max Damage";
            return $"+{galeBowPickupPierceBonus} Pierce  •  +{dmgPart:0.#} Max Damage";
        }
        if (heavenlyGaleEnabled && kind == BoostKind.Damage
            && pickupSourceTypeHint == eWeaponType.shield)
        {
            // Shield pickup specifically — arrow lifetime + damage. Show
            // both pieces unless lifetime bonus is capped.
            bool lifetimeFull = arrowLifetimeBonus >= maxArrowLifetimeBonus - 0.001f;
            float dmgPart = maxDamageIncreasePerLevel * Mathf.Max(0f, galeShieldPickupDamageScale);
            if (lifetimeFull)
                return $"+{dmgPart:0.#} Max Damage";
            return $"+{galeShieldPickupLifetimeBonus:0.##}s Arrow Lifetime  •  +{dmgPart:0.#} Max Damage";
        }
        if (heavenlyGaleEnabled && kind != BoostKind.Damage)
        {
            switch (kind)
            {
                case BoostKind.AttackSpeed:
                    if (galeBarrageInterval <= galeBarrageMinInterval + 0.0001f)
                        return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
                    {
                        float next = Mathf.Max(galeBarrageMinInterval,
                            galeBarrageInterval * Mathf.Clamp(galeBarrageIntervalPerBoost, 0.5f, 1f));
                        float pct = (galeBarrageInterval / next - 1f) * 100f;
                        return $"+{pct:0}% Barrage Fire Rate";
                    }
                case BoostKind.Range:
                    if (galeBarrageDuration >= galeBarrageMaxDuration - 0.001f)
                        return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
                    return $"+{galeBarrageDurationPerBoost:0.##}s Barrage Duration";
                case BoostKind.Projectiles:
                    if (galeBarrageProjectilesPerShot >= galeBarrageMaxProjectilesPerShot)
                        return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";
                    return $"+{galeBarrageProjectilesPerBoost} Arrow / Barrage Shot";
            }
        }

        switch (kind)
        {
            case BoostKind.Damage:
                {
                    float scale = IsBoostMaxed(BoostKind.Damage) ? postMaxBoostScale : 1f;
                    return $"+{maxDamageIncreasePerLevel * scale:0.#} Max Damage  •  +{deathBeamDpsPerBoost * scale:0.#} Beam DPS";
                }

            case BoostKind.Projectiles:
                {
                    // Crossbow pickup on Bow: extra arrow on the normal shot,
                    // longer death beam, plus the same damage/beam-DPS gain
                    // Damage gets. Each piece is dropped from the label once
                    // its own cap is reached so the button stays honest.
                    bool arrowFull = arrowProjectileCount >= maxArrowProjectileCount;
                    bool durFull   = deathBeamDurationBonus >= maxDeathBeamDurationBonus - 0.001f;
                    // The Bow only grants +1 Arrow every OTHER pickup. The
                    // NEXT pickup grants when the running counter is even
                    // (since incrementing it lands on an odd number).
                    bool nextGrantsArrow = !arrowFull && (crossbowPickupCount % 2 == 0);
                    float scale = IsDamageMaxed ? postMaxBoostScale : 1f;
                    string dmgPart = $"+{maxDamageIncreasePerLevel * scale:0.#} Max Damage  •  +{deathBeamDpsPerBoost * scale:0.#} Beam DPS";
                    string parts = dmgPart;
                    if (!durFull)         parts = $"+{deathBeamDurationPerBoost:0.##}s Beam  •  " + parts;
                    if (nextGrantsArrow)  parts = "+1 Arrow  •  " + parts;
                    return parts;
                }

            case BoostKind.AttackSpeed:
                {
                    // When both charge and overcharge are at their floors, fall back
                    // to scaled damage so the boost still does something visible.
                    float scale = IsBoostMaxed(BoostKind.AttackSpeed) ? postMaxBoostScale : 1f;
                    bool chargeFloor = fullChargeTime <= minFullChargeTime + 0.001f;
                    bool overFloor   = overchargeTime <= minOverchargeTime + 0.001f;
                    if (chargeFloor && overFloor)
                        return $"+{damageIncreasePerLevel * scale:0.#} Damage";

                    // Apply diminishing returns so the displayed gain shrinks per pickup.
                    float diminish = AttackSpeedDiminisher;
                    float effChargePct = chargeSpeedIncreasePercent     * diminish * scale;
                    float effOverPct   = overchargeSpeedIncreasePercent * diminish * scale;

                    float curC = Mathf.Max(minFullChargeTime, fullChargeTime);
                    float nxtC = Mathf.Max(minFullChargeTime, fullChargeTime / (1f + effChargePct));
                    float curO = Mathf.Max(minOverchargeTime, overchargeTime);
                    float nxtO = Mathf.Max(minOverchargeTime, overchargeTime / (1f + effOverPct));
                    bool chargeStuck = nxtC >= curC;
                    bool overStuck   = nxtO >= curO;
                    string chargePart = chargeStuck ? "Charge maxed" : $"+{(curC / nxtC - 1f) * 100f:0}% Charge";
                    string overPart   = overStuck   ? "Beam maxed"   : $"-{(curO - nxtO):0.0}s to Beam";
                    return chargePart + "  •  " + overPart;
                }

            case BoostKind.Range:
                {
                    if (IsBoostMaxed(BoostKind.Range))
                        return $"+{damageIncreasePerLevel * postMaxBoostScale:0.#} Damage";

                    // Show whichever pieces of the Range boost still have
                    // headroom — Max HP%, lifetime, and pierce cap independently.
                    bool maxHpFull    = deathBeamMaxHpPercentBonus >= maxDeathBeamMaxHpPercentBonus - 0.001f;
                    bool lifetimeFull = arrowLifetimeBonus  >= maxArrowLifetimeBonus  - 0.001f;
                    bool pierceFull   = arrowPierceBonus    >= maxArrowPierceBonus;
                    var parts = new System.Collections.Generic.List<string>(3);
                    if (!maxHpFull)    parts.Add($"+{deathBeamMaxHpPercentBonusPerBoost:0.##}% Beam Max HP Dmg");
                    if (!lifetimeFull) parts.Add($"+{arrowLifetimeBonusPerBoost:0.##}s Arrow Lifetime");
                    if (!pierceFull)   parts.Add($"+{arrowPierceBonusPerBoost} Pierce");
                    return string.Join("  •  ", parts);
                }
        }
        return base.DescribeBoost(kind);
    }

    public override string GetExtraStatsBlock()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Arrow Dmg: {arrowMinDamage:0.#}–{arrowMaxDamage:0.#}");
        sb.Append($"\nArrows / Shot: {arrowProjectileCount}");
        sb.Append($"\nFull Charge: {fullChargeTime:0.##}s");
        sb.Append($"\nTime to Beam: {overchargeTime:0.##}s");
        if (deathBeamDpsBonus > 0f)      sb.Append($"\nBeam DPS Bonus: +{deathBeamDpsBonus:0.#}");
        // Beam Max HP/s damage: prefab base (always shown) plus any Range-boost
        // bonus from grenade pickups (only shown if non-zero).
        if (BeamUsesMaxHpDamage)
        {
            float basePct = deathBeamPrefab.maxHpFractionPerSecond * 100f;
            float totalPct = basePct + deathBeamMaxHpPercentBonus;
            if (deathBeamMaxHpPercentBonus > 0f)
                sb.Append($"\nBeam Max HP/s: {totalPct:0.##}%  (base {basePct:0.##}% + {deathBeamMaxHpPercentBonus:0.##}%)");
            else
                sb.Append($"\nBeam Max HP/s: {basePct:0.##}%");
        }
        if (deathBeamRadiusBonus > 0f)   sb.Append($"\nBeam Radius +{deathBeamRadiusBonus:0.##}");
        // Show the total arrow lifetime (base + bonus) since the bow
        // explicitly controls the base value now. Always rendered so the
        // player can see how long their arrows stay alive.
        sb.Append($"\nArrow Lifetime: {arrowBaseLifetime + arrowLifetimeBonus:0.##}s");
        if (arrowPierceBonus > 0)        sb.Append($"\nArrow Pierce +{arrowPierceBonus}");
        if (deathBeamDurationBonus > 0f) sb.Append($"\nBeam Duration +{deathBeamDurationBonus:0.##}s");
        sb.Append($"\nOvercharge Rate: +{postOverchargeDamageRate * 100f:0}%/s");
        return sb.ToString();
    }

    /// <summary>
    /// Apply a single damage upgrade to ALL bow damage paths (arrows + beam).
    /// Centralized so post-max fallbacks (capped Range, AttackSpeed at floor)
    /// route to the same fields as the regular Damage boost — Fire() reads
    /// arrowMin/MaxDamage and deathBeamDpsBonus, NOT the base `damage` field,
    /// so any code that just adds to `damage` would be a silent no-op.
    /// </summary>
    private void ApplyBowDamageBoost(float scale)
    {
        damageLevel++;
        damage             += damageIncreasePerLevel    * scale;
        arrowMinDamage     += minDamageIncreasePerLevel * scale;
        arrowMaxDamage     += maxDamageIncreasePerLevel * scale;
        deathBeamDpsBonus  += deathBeamDpsPerBoost      * scale;
    }

    public override bool TryApplyBoost(BoostKind kind)
    {
        // Route to the Heavenly Gale variant when active. Damage usually
        // stays on the standard pipeline (still buffs arrowMin/Max + beam
        // DPS, matching the spec "Normal damage bonuses increase both
        // barrage damage and death beam damage") — UNLESS the source
        // pickup is a bow specifically, in which case we apply pierce +
        // minor damage instead. Sword / shield pickups also map to
        // BoostKind.Damage but are NOT bow-typed, so they continue to
        // use the regular damage path.
        if (heavenlyGaleEnabled
            && kind == BoostKind.Damage
            && pickupSourceTypeHint == eWeaponType.bow)
        {
            return TryApplyHeavenlyGaleBowPickup();
        }
        if (heavenlyGaleEnabled
            && kind == BoostKind.Damage
            && pickupSourceTypeHint == eWeaponType.shield)
        {
            return TryApplyHeavenlyGaleShieldPickup();
        }
        if (heavenlyGaleEnabled && kind != BoostKind.Damage)
        {
            return TryApplyHeavenlyGaleBoost(kind);
        }

        switch (kind)
        {
            case BoostKind.Damage:
                {
                    float scale = IsBoostMaxed(BoostKind.Damage) ? postMaxBoostScale : 1f;
                    ApplyBowDamageBoost(scale);
                    return true;
                }

            case BoostKind.Projectiles:
                {
                    // Crossbow pickup on Bow. Every other upgrade grants
                    // +1 Arrow; the rest of the boost (beam duration,
                    // damage, beam DPS) applies on every pickup.
                    crossbowPickupCount++;
                    bool grantArrow = (crossbowPickupCount % 2 == 1)
                                       && arrowProjectileCount < maxArrowProjectileCount;
                    if (grantArrow) arrowProjectileCount++;

                    deathBeamDurationBonus = Mathf.Min(maxDeathBeamDurationBonus,
                        deathBeamDurationBonus + deathBeamDurationPerBoost);

                    float scale = IsDamageMaxed ? postMaxBoostScale : 1f;
                    ApplyBowDamageBoost(scale);
                    return true;
                }

            case BoostKind.AttackSpeed:
                {
                    float scale = IsBoostMaxed(BoostKind.AttackSpeed) ? postMaxBoostScale : 1f;
                    bool chargeFloor = fullChargeTime <= minFullChargeTime + 0.001f;
                    bool overFloor   = overchargeTime <= minOverchargeTime + 0.001f;
                    if (chargeFloor && overFloor)
                    {
                        // Both floors hit — convert to scaled damage. Route to
                        // the SAME fields the regular Damage boost touches
                        // (arrow min/max, beam DPS bonus); the base `damage`
                        // field is unused by Fire(), so adding to it would be
                        // a silent no-op.
                        ApplyBowDamageBoost(scale);
                        return true;
                    }

                    // Apply diminishing returns: each subsequent AttackSpeed
                    // boost is weaker than the last (shared counter with the
                    // base class so weapon swaps don't reset the curve).
                    float diminish = AttackSpeedDiminisher;
                    float effChargePct = chargeSpeedIncreasePercent     * diminish * scale;
                    float effOverPct   = overchargeSpeedIncreasePercent * diminish * scale;

                    fullChargeTime = Mathf.Max(minFullChargeTime, fullChargeTime / (1f + effChargePct));
                    overchargeTime = Mathf.Max(minOverchargeTime, overchargeTime / (1f + effOverPct));
                    // Faster charging also stacks the post-overcharge damage
                    // rate, so upgrades let the player accumulate beam damage
                    // faster while held past the death-beam threshold (also
                    // diminished so it doesn't run away).
                    postOverchargeDamageRate *= (1f + effChargePct);

                    attackSpeedBoostsTaken++;
                    return true;
                }

            case BoostKind.Range:
                rangeBoostsTaken++;
                if (IsBoostMaxed(BoostKind.Range))
                {
                    // Both pieces capped — fall back to scaled damage on the
                    // SAME fields the regular Damage boost touches (arrow
                    // min/max, beam DPS bonus). Adding to `damage` would have
                    // no effect since Fire() reads from arrow/beam fields.
                    ApplyBowDamageBoost(postMaxBoostScale);
                    return true;
                }
                // Bump every Range piece toward its cap. Mathf.Min keeps each
                // one from overshooting — capped pieces stop changing while
                // the others can keep growing.
                deathBeamMaxHpPercentBonus = Mathf.Min(maxDeathBeamMaxHpPercentBonus,
                    deathBeamMaxHpPercentBonus + deathBeamMaxHpPercentBonusPerBoost);
                arrowLifetimeBonus = Mathf.Min(maxArrowLifetimeBonus,
                    arrowLifetimeBonus + arrowLifetimeBonusPerBoost);
                arrowPierceBonus = Mathf.Min(maxArrowPierceBonus,
                    arrowPierceBonus + arrowPierceBonusPerBoost);
                return true;
        }
        return base.TryApplyBoost(kind);
    }

    /// <summary>
    /// Heavenly Gale's boost handler. Each kind redirects to a barrage
    /// parameter instead of the bow's normal stat field:
    ///   * AttackSpeed (Dagger) → multiplicative reduction on barrage
    ///     fire interval (faster shots during the barrage).
    ///   * Range (Grenade) → additive extension on barrage duration.
    ///   * Projectiles (Crossbow) → +N arrows fired per barrage shot.
    /// Damage stays on the regular path (handled by the caller's switch
    /// before this method is invoked).
    /// </summary>
    private bool TryApplyHeavenlyGaleBoost(BoostKind kind)
    {
        switch (kind)
        {
            case BoostKind.AttackSpeed:
                attackSpeedBoostsTaken++;
                if (galeBarrageInterval <= galeBarrageMinInterval + 0.0001f)
                {
                    // Already at the fastest fire rate — convert post-cap
                    // pickups to scaled damage so the player still gets
                    // progress on the same fields the regular Damage boost
                    // touches (arrow min/max + beam DPS bonus).
                    ApplyBowDamageBoost(postMaxBoostScale);
                    return true;
                }
                galeBarrageInterval = Mathf.Max(galeBarrageMinInterval,
                    galeBarrageInterval * Mathf.Clamp(galeBarrageIntervalPerBoost, 0.5f, 1f));
                return true;

            case BoostKind.Range:
                rangeBoostsTaken++;
                if (galeBarrageDuration >= galeBarrageMaxDuration - 0.001f)
                {
                    ApplyBowDamageBoost(postMaxBoostScale);
                    return true;
                }
                galeBarrageDuration = Mathf.Min(galeBarrageMaxDuration,
                    galeBarrageDuration + Mathf.Max(0f, galeBarrageDurationPerBoost));
                return true;

            case BoostKind.Projectiles:
                crossbowPickupCount++;
                if (galeBarrageProjectilesPerShot >= galeBarrageMaxProjectilesPerShot)
                {
                    ApplyBowDamageBoost(postMaxBoostScale);
                    return true;
                }
                galeBarrageProjectilesPerShot = Mathf.Min(galeBarrageMaxProjectilesPerShot,
                    galeBarrageProjectilesPerShot + Mathf.Max(0, galeBarrageProjectilesPerBoost));
                return true;
        }
        // Damage falls through to the regular handler — caller already
        // routed Damage straight there before calling us. Anything else
        // unknown defers to base.
        return base.TryApplyBoost(kind);
    }

    /// <summary>
    /// Heavenly Gale's bow-pickup variant. Bow / Sword / Shield pickups
    /// all map to BoostKind.Damage, but under Heavenly Gale a BOW pickup
    /// specifically grants extra pierce on the barrage arrows plus a
    /// minor damage bump (scaled by galeBowPickupDamageScale). Sword /
    /// shield pickups still take the regular full Damage path.
    /// </summary>
    private bool TryApplyHeavenlyGaleBowPickup()
    {
        // Pierce — stacks via the existing arrowPierceBonus path that
        // FireGaleBarrageShot reads at spawn time, so each barrage arrow
        // immediately benefits from the extra pierce.
        arrowPierceBonus = Mathf.Min(maxArrowPierceBonus,
            arrowPierceBonus + Mathf.Max(0, galeBowPickupPierceBonus));
        // Minor damage gain — same fields a regular damage boost would
        // touch (arrowMin/Max + beam DPS), but at a smaller scale. The
        // fraction is configurable via galeBowPickupDamageScale (default
        // 0.5 = half a normal damage pickup).
        ApplyBowDamageBoost(Mathf.Max(0f, galeBowPickupDamageScale));
        return true;
    }

    /// <summary>
    /// Heavenly Gale's shield-pickup variant. Shield maps to BoostKind.Damage
    /// like sword/bow, but under Heavenly Gale the shield pickup specifically
    /// extends arrow lifetime (longer-flying barrage arrows that can chain
    /// through enemies further) on top of a configurable damage gain.
    /// </summary>
    private bool TryApplyHeavenlyGaleShieldPickup()
    {
        arrowLifetimeBonus = Mathf.Min(maxArrowLifetimeBonus,
            arrowLifetimeBonus + Mathf.Max(0f, galeShieldPickupLifetimeBonus));
        ApplyBowDamageBoost(Mathf.Max(0f, galeShieldPickupDamageScale));
        return true;
    }

    /// <summary>
    /// Activate Heavenly Gale: reset bow stats to their from-fresh
    /// originals, then re-apply every boost the player accumulated under
    /// the new Heavenly Gale pipeline. The result is identical to what
    /// the bow would look like if Heavenly Gale had been active from the
    /// start AND every previously-collected powerup had been picked up
    /// in the same order.
    /// </summary>
    public void ApplyHeavenlyGale()
    {
        if (heavenlyGaleEnabled) return;
        heavenlyGaleEnabled = true;

        // Capture how many of each boost the player has taken so far.
        // damageLevel and crossbowPickupCount are bumped on TryApplyBoost
        // for their respective kinds; attackSpeedBoostsTaken lives on the
        // base class; rangeBoostsTaken is bumped above.
        int dmgBoosts        = damageLevel;
        int speedBoosts      = attackSpeedBoostsTaken;
        int rangeBoosts      = rangeBoostsTaken;
        int projBoosts       = crossbowPickupCount;

        // Reset the BOW's regular stat fields back to their snapshot
        // values. The fields touched by AttackSpeed (charge times),
        // Range (beam %, lifetime, pierce), Projectiles (arrow count,
        // beam duration) and Damage (arrow min/max, beam DPS bonus) all
        // get returned to "as if no boosts had been applied yet".
        // The Heavenly Gale charge time replaces fullChargeTime
        // permanently — that's the whole point of the augment.
        fullChargeTime              = Mathf.Max(0.05f, galeChargeTime);
        // Set overcharge ridiculously high so the regular death beam
        // path is never reached on release; HG always fires the barrage.
        overchargeTime              = 9999f;
        postOverchargeDamageRate    = 0f;          // accumulator unused under HG
        arrowMinDamage              = origArrowMinDamage;
        arrowMaxDamage              = origArrowMaxDamage;
        deathBeamDpsBonus           = 0f;
        deathBeamMaxHpPercentBonus  = 0f;
        deathBeamRadiusBonus        = 0f;
        deathBeamDurationBonus      = 0f;
        arrowLifetimeBonus          = 0f;
        arrowPierceBonus            = 0;
        arrowProjectileCount        = 1;            // HG re-routes the per-shot count to galeBarrageProjectilesPerShot
        damage                      = OriginalDamage;
        // Reset counters — we'll re-increment them as we re-apply each
        // boost below, so no double-counting.
        damageLevel                 = 0;
        attackSpeedBoostsTaken      = 0;
        crossbowPickupCount         = 0;
        rangeBoostsTaken            = 0;

        // Initialize barrage params at their baselines. TryApplyHeavenlyGaleBoost
        // mutates these in place each time it runs.
        galeBarrageDuration            = Mathf.Min(galeBarrageMaxDuration, Mathf.Max(0.05f, galeBarrageBaseDuration));
        galeBarrageInterval            = Mathf.Max(galeBarrageMinInterval, galeBarrageBaseInterval);
        galeBarrageProjectilesPerShot  = Mathf.Clamp(galeBarrageBaseProjectilesPerShot, 1, galeBarrageMaxProjectilesPerShot);

        // Re-apply each previously-collected boost under the NEW pipeline
        // so the player's progression carries over. Damage boosts go
        // through the regular Damage handler (which still buffs arrows
        // and the beam DPS — the same fields HG reads when firing).
        for (int i = 0; i < dmgBoosts; i++)        TryApplyBoost(BoostKind.Damage);
        for (int i = 0; i < speedBoosts; i++)      TryApplyBoost(BoostKind.AttackSpeed);
        for (int i = 0; i < rangeBoosts; i++)      TryApplyBoost(BoostKind.Range);
        for (int i = 0; i < projBoosts; i++)       TryApplyBoost(BoostKind.Projectiles);

        Debug.Log($"[Heavenly Gale] Activated. Re-applied dmg={dmgBoosts} speed={speedBoosts} range={rangeBoosts} proj={projBoosts} → barrage(duration={galeBarrageDuration:0.##}s, interval={galeBarrageInterval:0.##}s, projectiles={galeBarrageProjectilesPerShot}).");
    }

    public override bool OnFireDown(Hero owner)
    {
        // Block new charges while the previously-fired death beam is still
        // alive — the player has to wait for the beam to finish before the
        // next shot can begin. activeBeam compares to "fake null" once the
        // beam GameObject is destroyed, so this clears itself automatically.
        if (activeBeam != null) return false;
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

        float chargeT = Mathf.Clamp01(chargeTime / fullChargeTime);

        // Hero aura: Lightning when fully charged. Spawn ONCE at the
        // chargeT=1 transition; reset flag is in EndCharge so each charge
        // cycle gets a fresh trigger.
        if (chargeT >= 1f && !fullyChargedAuraReached)
        {
            fullyChargedAuraReached = true;
            if (fullyChargedHeroAuraPrefab != null)
                fullyChargedHeroAuraInstance = SpawnHeroAura(owner, fullyChargedHeroAuraPrefab);
            // Heavenly Gale: stack the overcharged aura on top at the
            // SAME moment the charged aura spawns. The augment doesn't
            // use overcharge as a separate state, so the two auras play
            // together as a single "fully charged Heavenly Gale" cue.
            if (heavenlyGaleEnabled && !overchargeReached && overchargedHeroAuraPrefab != null)
            {
                overchargeReached = true;
                overchargedHeroAuraInstance = SpawnHeroAura(owner, overchargedHeroAuraPrefab);
            }
        }

        if (!heavenlyGaleEnabled && chargeTime >= overchargeTime && !overchargeReached)
        {
            overchargeReached = true;
            if (overchargeVfxPrefab != null && bowVisualInstance != null)
                overchargeVfxInstance = Instantiate(overchargeVfxPrefab, bowVisualInstance.transform.position, bowVisualInstance.transform.rotation, bowVisualInstance.transform);

            // Hero aura: Plexus stacked ON TOP of the still-active Lightning
            // aura. Two distinct instances; both get torn down in EndCharge.
            if (overchargedHeroAuraPrefab != null)
                overchargedHeroAuraInstance = SpawnHeroAura(owner, overchargedHeroAuraPrefab);
        }

        // Past overcharge → accumulate beam damage multiplier indefinitely.
        // Each second adds postOverchargeDamageRate to the multiplier; the
        // beam's damagePerSecond is multiplied by (1 + accumulated) on fire.
        if (overchargeReached && postOverchargeDamageRate > 0f)
        {
            postOverchargeAccumulated += postOverchargeDamageRate * Time.deltaTime;
            if (maxPostOverchargeMultiplier > 0f)
                postOverchargeAccumulated = Mathf.Min(maxPostOverchargeMultiplier, postOverchargeAccumulated);
        }

        return false;
    }

    public override void OnInterrupted(Hero owner)
    {
        if (charging) EndCharge(owner);
        // Also kill any beam that's currently mid-fire — interrupts now
        // come from the level-up UI as well as dashing / powerup pickup,
        // and a beam ticking through a paused level-up screen looks like
        // a stuck visual to the player.
        if (activeBeam != null)
        {
            Destroy(activeBeam.gameObject);
            activeBeam = null;
        }
    }

    public override bool OnFireUp(Hero owner)
    {
        if (!charging) return false;
        bool fired = false;

        if (chargeTime >= minReleaseTime)
        {
            if (heavenlyGaleEnabled)
            {
                // Heavenly Gale: a fully-charged release fires the barrage.
                // Below-full releases do nothing (the player has to commit
                // to the full 3s charge to get the augment's payoff).
                if (chargeTime >= fullChargeTime)
                {
                    StartHeavenlyGaleBarrage(owner);
                    fired = true;
                }
            }
            else
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
        }

        EndCharge(owner);
        return fired;
    }

    // ---- Helpers ----

    /// <summary>
    /// Instantiate a hero-aura prefab centered on the hero with the configured
    /// y-offset and uniform scale. Parented to the hero so it follows movement
    /// for the rest of the charge window. Colliders on the prefab are disabled
    /// so the aura can't shove the hero or get caught by the dash phase-through.
    /// </summary>
    private GameObject SpawnHeroAura(Hero owner, GameObject prefab)
    {
        if (prefab == null || owner == null) return null;
        Vector3 pos = owner.transform.position + Vector3.up * heroAuraYOffset;
        GameObject go = Instantiate(prefab, pos, owner.transform.rotation, owner.transform);
        go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, heroAuraScale);
        // Disable any colliders so the aura is purely cosmetic.
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        return go;
    }

    private void StartCharge(Hero owner)
    {
        charging = true;
        chargeTime = 0f;
        overchargeReached = false;
        fullyChargedAuraReached = false;
        postOverchargeAccumulated = 0f; // fresh charge → no banked extra-damage
        owner.speedMultiplier = slowdownWhileCharging;

        if (bowVisualPrefab != null)
        {
            Vector3 pos = owner.transform.position + owner.transform.forward * bowSpawnDistance + Vector3.up * bowSpawnHeight;
            bowVisualInstance = Instantiate(bowVisualPrefab, pos, owner.transform.rotation * Quaternion.Euler(bowVisualRotationOffset));
            // Disable any colliders on the bow visual so it can't shove the hero.
            foreach (var c in bowVisualInstance.GetComponentsInChildren<Collider>()) c.enabled = false;
        }
    }

    private void EndCharge(Hero owner)
    {
        charging = false;
        chargeTime = 0f;
        overchargeReached = false;
        fullyChargedAuraReached = false;
        postOverchargeAccumulated = 0f; // banked damage is consumed/discarded by FireDeathBeam or the interrupt
        owner.speedMultiplier = 1f;
        if (bowVisualInstance != null) Destroy(bowVisualInstance);
        if (overchargeVfxInstance != null) Destroy(overchargeVfxInstance);
        if (fullyChargedHeroAuraInstance != null) Destroy(fullyChargedHeroAuraInstance);
        if (overchargedHeroAuraInstance != null) Destroy(overchargedHeroAuraInstance);
        fullyChargedHeroAuraInstance = null;
        overchargedHeroAuraInstance = null;
    }

    private void FireArrow(Hero owner)
    {
        float chargeT = Mathf.Clamp01(chargeTime / fullChargeTime);
        float dmg = Mathf.Lerp(arrowMinDamage, arrowMaxDamage, chargeT);
        float scale = Mathf.Lerp(arrowMinScale, arrowMaxScale, chargeT);

        // Apply hero damage multipliers + crit roll for this shot. One roll
        // for the whole volley so all arrows in the fan share it. WithCrit
        // overload feeds the Meteor general augment.
        dmg = owner.ComputeAttackDamageWithCrit(dmg, out bool wasCrit);
        // Single meteor roll shared across every arrow in the fan — the
        // shared MeteorRoll's `consumed` flag guarantees only the first
        // enemy hit across the whole volley fires the meteor, while every
        // arrow remains a candidate (so one arrow missing doesn't waste
        // the meteor opportunity).
        MeteorRoll meteorRoll = owner.TryRollMeteor(dmg, wasCrit, enemyLayers);

        // Fully-charged shots gain homing — they curve onto the nearest
        // enemy with near-perfect tracking, then chain to a new target if
        // their current one dies. Piercing is preserved by the prefab.
        bool fullyCharged = chargeT >= homingChargeThreshold;

        Vector3 spawn = owner.transform.position + owner.transform.forward * arrowSpawnForward + Vector3.up * bowSpawnHeight;

        // Total lifetime for this shot — bow controls the base explicitly
        // (overrides the prefab's value) and stacks the Range-boost bonus.
        float effLifetime = Mathf.Max(0.01f, arrowBaseLifetime + Mathf.Max(0f, arrowLifetimeBonus));

        // Damage falloff lives on the Projectile, but the bow controls the
        // policy — so push the inspector values down onto every spawned arrow.
        float falloffPerHit = Mathf.Clamp01(arrowDamageFalloffPerHit);
        float falloffFloor  = Mathf.Clamp01(arrowDamageFalloffFloor);

        int n = Mathf.Max(1, arrowProjectileCount);
        if (n == 1)
        {
            Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
            p.transform.localScale = arrowPrefab.transform.localScale * scale;
            if (arrowSpeed > 0f) p.speed = arrowSpeed;
            ApplyHomingIfCharged(p, fullyCharged);
            // Lifetime + pierce bonuses must be set BEFORE Launch — Launch
            // snapshots pierceCount and schedules Destroy(gameObject, lifetime).
            // Pierce bonus is only applied to non-infinite arrows so the
            // fully-charged shot's infinite-pierce override is preserved.
            p.lifetime = effLifetime;
            if (arrowPierceBonus > 0 && p.pierceCount >= 0) p.pierceCount += arrowPierceBonus;
            p.damageFalloffPerHit = falloffPerHit;
            p.damageFalloffFloor  = falloffFloor;
            p.Launch(owner.transform.forward, dmg, enemyLayers);
            if (meteorRoll != null) owner.AttachMeteorRoll(p.gameObject, meteorRoll);
            return;
        }

        // Fan the arrows evenly across [-spread/2, +spread/2] around forward.
        // Same shape as the Crossbow's multi-shot, so the upgrade reads the
        // same way visually whether you're holding a Bow or a Crossbow.
        float half = arrowSpreadAngle * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            float angle = Mathf.Lerp(-half, half, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * owner.transform.forward;
            Projectile p = Instantiate(arrowPrefab, spawn, Quaternion.identity);
            p.transform.localScale = arrowPrefab.transform.localScale * scale;
            if (arrowSpeed > 0f) p.speed = arrowSpeed;
            ApplyHomingIfCharged(p, fullyCharged);
            p.lifetime = effLifetime;
            if (arrowPierceBonus > 0 && p.pierceCount >= 0) p.pierceCount += arrowPierceBonus;
            p.damageFalloffPerHit = falloffPerHit;
            p.damageFalloffFloor  = falloffFloor;
            p.Launch(dir, dmg, enemyLayers);
            // Every arrow in the fan gets the SAME meteor roll — the
            // shared `consumed` flag inside MeteorRoll ensures only the
            // first hit across the whole volley fires the meteor while
            // every arrow is still a candidate (so a missed leftmost
            // arrow doesn't waste the opportunity).
            if (meteorRoll != null) owner.AttachMeteorRoll(p.gameObject, meteorRoll);
        }
    }

    // ===================== Heavenly Gale =====================

    /// <summary>
    /// Kick off the Heavenly Gale barrage from a fully-charged release.
    /// All barrage params (duration, fire interval, projectiles per shot)
    /// are already resolved by ApplyHeavenlyGale + accumulated boosts;
    /// this coroutine just fires arrows on cadence with oscillating angles.
    /// </summary>
    private void StartHeavenlyGaleBarrage(Hero owner)
    {
        if (galeBarrageActive) return;
        galeBarrageActive = true;
        StartCoroutine(HeavenlyGaleBarrageCoroutine(owner));
    }

    private IEnumerator HeavenlyGaleBarrageCoroutine(Hero owner)
    {
        float duration = Mathf.Max(0.05f, galeBarrageDuration);
        float interval = Mathf.Max(galeBarrageMinInterval, galeBarrageInterval);
        int   perShot  = Mathf.Clamp(galeBarrageProjectilesPerShot, 1, galeBarrageMaxProjectilesPerShot);
        // How many discrete angle steps fit in ±envelope. With 50° and step
        // 10° you get steps {0,10,20,30,40,50} per side. We ping-pong the
        // index between [-stepsPerSide..+stepsPerSide].
        float envelope = Mathf.Max(0f, galeOscillationDegreesPerSide);
        float stepDeg  = Mathf.Max(0.01f, galeOscillationStepDegrees);
        int   stepsPerSide = Mathf.Max(1, Mathf.RoundToInt(envelope / stepDeg));
        int   currentStep  = -stepsPerSide; // start at the leftmost extreme
        int   direction    = 1;             // sweeping toward +max

        float endTime = Time.time + duration;
        while (Time.time < endTime && owner != null && !owner.IsDead)
        {
            // Fire one barrage shot at the current oscillation angle.
            float angle = currentStep * stepDeg;
            FireGaleBarrageShot(owner, angle, perShot);

            // Advance the oscillation step, ping-pong at boundaries.
            currentStep += direction;
            if (currentStep > stepsPerSide)  { currentStep = stepsPerSide - 1;  direction = -1; }
            if (currentStep < -stepsPerSide) { currentStep = -stepsPerSide + 1; direction = 1;  }

            yield return new WaitForSeconds(interval);
        }
        galeBarrageActive = false;
    }

    /// <summary>
    /// Fire a single barrage "shot" — perShot arrows in a tight fan around
    /// the given centerAngle (with a small per-arrow jitter so multiple
    /// arrows don't visually stack). Each arrow is fully homing and runs
    /// through the same Projectile pipeline as the regular bow's
    /// fully-charged shots, so they curve onto enemies. After firing the
    /// shot, each arrow independently rolls galeDeathBeamChancePerArrow
    /// to additionally call down a sky death beam on a random alive enemy.
    /// </summary>
    private void FireGaleBarrageShot(Hero owner, float centerAngleDeg, int perShot)
    {
        // Prefer the custom Heavenly Gale arrow prefab if assigned; fall
        // back to the regular arrowPrefab so the augment doesn't soft-fail
        // before the player has wired up the dedicated visual.
        Projectile prefab = galeBarrageArrowPrefab != null ? galeBarrageArrowPrefab : arrowPrefab;
        if (owner == null || prefab == null) return;

        Vector3 spawn = owner.transform.position + owner.transform.forward * arrowSpawnForward + Vector3.up * bowSpawnHeight;
        // Arrow damage uses the bow's MAX-charge damage (the player paid
        // the full 3s charge to get here), then routes through the hero's
        // attack pipeline so damageMultiplier + crit + Meteor augment
        // hooks all apply per shot.
        float baseDmg = arrowMaxDamage;
        float scale = arrowMaxScale;
        float effLifetime = Mathf.Max(0.01f, arrowBaseLifetime + Mathf.Max(0f, arrowLifetimeBonus));
        float falloffPerHit = Mathf.Clamp01(arrowDamageFalloffPerHit);
        float falloffFloor  = Mathf.Clamp01(arrowDamageFalloffFloor);

        // Tiny per-arrow jitter inside the shot so 3 arrows at the same
        // centerAngle don't draw on top of each other.
        float jitterPerSide = (perShot > 1) ? Mathf.Min(stepDegSafeJitter, galeOscillationStepDegrees * 0.4f) : 0f;

        for (int i = 0; i < perShot; i++)
        {
            float t = (perShot == 1) ? 0.5f : (float)i / (perShot - 1);
            float jitter = (perShot == 1) ? 0f : Mathf.Lerp(-jitterPerSide, jitterPerSide, t);
            float angle = centerAngleDeg + jitter;
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * owner.transform.forward;

            float dmg = owner.ComputeAttackDamageWithCrit(baseDmg, out bool wasCrit);
            MeteorRoll meteorRoll = owner.TryRollMeteor(dmg, wasCrit, enemyLayers);

            Projectile p = Instantiate(prefab, spawn, Quaternion.identity);
            p.transform.localScale = prefab.transform.localScale * scale;
            if (arrowSpeed > 0f) p.speed = arrowSpeed;
            // Heavenly Gale arrows are ALWAYS homing — that's part of the
            // augment fantasy. Reuse the existing fully-charged setup so
            // tuning carries over.
            ApplyHomingIfCharged(p, true);
            p.lifetime = effLifetime;
            if (arrowPierceBonus > 0 && p.pierceCount >= 0) p.pierceCount += arrowPierceBonus;
            // HG-specific pierce cap — clamps base + bonus so a stacked
            // bow-pickup loadout can't make a single arrow chain through
            // every enemy on the screen. -1 (infinite from the fully-
            // charged setup) is preserved by the >= 0 guard so the cap
            // only narrows finite values.
            if (p.pierceCount >= 0 && galeBarrageMaxPierce >= 0)
                p.pierceCount = Mathf.Min(p.pierceCount, galeBarrageMaxPierce);
            p.damageFalloffPerHit = falloffPerHit;
            p.damageFalloffFloor  = falloffFloor;
            p.Launch(dir, dmg, enemyLayers);
            if (meteorRoll != null) owner.AttachMeteorRoll(p.gameObject, meteorRoll);

            // Random rainbow trail tint per arrow — full saturation +
            // value (punchy), low alpha so multiple overlapping arrow
            // trails read as a translucent streak instead of a solid blob.
            // Routed through Tiny.Trail.RuntimeTintColor (same hook the
            // Zenith sword uses) so the trail's mesh-vertex color picks
            // it up at the next emit.
            Color trailTint = Color.HSVToRGB(Random.value, 0.9f, 1f);
            trailTint.a = Mathf.Clamp01(galeArrowTrailAlpha);
            foreach (var trail in p.GetComponentsInChildren<Tiny.Trail>(true))
                trail.RuntimeTintColor = trailTint;

            // Sky beam armer — attached so the per-hit chance is rolled
            // when the arrow ACTUALLY lands on an enemy, not the moment
            // we release the bow. Piercing arrows roll independently per
            // hit, so a fan of long arrows can chain multiple beams
            // through a packed group.
            if (galeDeathBeamChancePerArrow > 0f && deathBeamPrefab != null)
            {
                var skyArmer = p.gameObject.AddComponent<SkyBeamArmer>();
                skyArmer.source = this;
                skyArmer.owner = owner;
                skyArmer.chancePerHit = galeDeathBeamChancePerArrow;
            }
        }
    }

    // Small jitter ceiling so the perShot fan never spreads more than a
    // fraction of a step. Pure constant; declared as a field-style local
    // for readability up in the call site.
    private const float stepDegSafeJitter = 4f;

    /// <summary>
    /// Spawn a vertical death beam from the sky onto a random alive enemy
    /// inside galeDeathBeamTargetSearchRadius around the hero. Suppressed
    /// (no-op) if no enemy is in range — no fallback random landing spot
    /// because a sky beam striking empty ground has no payoff.
    /// </summary>
    /// <summary>
    /// Spawn a Heavenly Gale sky death beam on the given target enemy.
    /// Called by <see cref="SkyBeamArmer.OnEnemyHit"/> when a barrage
    /// arrow lands AND its per-hit chance roll passes — so the beam
    /// fires on the enemy that was actually struck rather than at a
    /// random target the moment the player releases the bow.
    /// </summary>
    public void SpawnHeavenlyGaleSkyBeamOn(Hero owner, Enemy target)
    {
        if (deathBeamPrefab == null || owner == null) return;
        if (target == null || target.IsDead) return;

        // Random rainbow hue per cast — full saturation + value + alpha=1
        // so DeathBeam.ApplyTintToRenderers actually overrides the
        // material color. The emission color gets boosted to HDR via
        // emissionBloomIntensity inside DeathBeam, which is what makes
        // the post-process bloom kick in on the beam.
        Color tint = Color.HSVToRGB(Random.value, 0.85f, 1f);
        tint.a = 1f;

        // Spawn an instance, then configure as a fixed sky beam BEFORE
        // calling Init: we set position + rotation on the transform so
        // the OverlapCapsule in DeathBeam.Update reads the correct world
        // pose, scale-down for the "thin" look, and apply the tint.
        DeathBeam beam = Instantiate(deathBeamPrefab);
        beam.useFixedTransform = true;
        beam.tintColor = tint;
        beam.emissionBloomIntensity = Mathf.Max(0f, galeDeathBeamBloomIntensity);
        // Override the prefab's authored shake — sky beams fire often and
        // the prefab's full-strength shake stacks into screen-rattling
        // territory. galeDeathBeamShakeAmplitude defaults to a much
        // smaller value so individual beams feel punchy without the
        // barrage as a whole becoming unreadable.
        beam.shakeAmplitude = Mathf.Max(0f, galeDeathBeamShakeAmplitude);
        // Per spec — 2.5s duration, scaled radius for a thin beam.
        beam.duration = Mathf.Max(0.05f, galeDeathBeamDuration);
        beam.radius   = beam.radius * Mathf.Max(0.05f, galeDeathBeamRadiusMultiplier);
        // Beam length = configured sky height. Setting this BEFORE Init
        // makes the localScale stretch line up with the actual capsule
        // length used for damage detection.
        beam.range    = Mathf.Max(1f, galeDeathBeamSkyHeight);
        // Damage gets a one-time hero-stat scaling at fire time, same as
        // the regular FireDeathBeam path. Independent crit roll per beam.
        beam.damagePerSecond = owner.ComputeAttackDamage(beam.damagePerSecond + deathBeamDpsBonus);
        // Keep the beam's max-HP-per-second damage as the prefab's base
        // (any HG-mode Range-boost bonus was wiped by ApplyHeavenlyGale's
        // reset, so we don't add deathBeamMaxHpPercentBonus here).

        // Random "from the sky" angle: tilt in [min..max] from
        // straight-down, in a uniformly random horizontal direction.
        // Result: the beam's bottom end punches into the ground past
        // the target, but the beam itself comes in at a different slant
        // each cast — so a burst of beams visually fans out across the
        // sky rather than all dropping on the same axis.
        float minTilt = Mathf.Min(galeDeathBeamMinTiltDegrees, galeDeathBeamMaxTiltDegrees);
        float maxTilt = Mathf.Max(galeDeathBeamMinTiltDegrees, galeDeathBeamMaxTiltDegrees);
        float tilt = Random.Range(minTilt, maxTilt);
        float yaw  = Random.Range(0f, 360f);
        Vector3 tiltAxis = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        Vector3 fwd = Quaternion.AngleAxis(tilt, tiltAxis) * Vector3.down;

        // Anchor: target sits NEAR the beam's bottom, but the beam
        // continues past the target by galeDeathBeamGroundExtension so
        // the visual reads as a beam striking THROUGH the ground rather
        // than ending exactly at the enemy's feet. Without this, tilted
        // beams look like they "land" awkwardly at the target's body.
        //
        // Geometry: DeathBeam.Update in fixed mode reads
        //   origin = transform.position - fwd*(range/2)
        // and the capsule extends [origin .. origin + fwd*range].
        // We want the capsule to span (range - extension) above target
        // and (extension) past target along fwd, so:
        //   origin = target - fwd * (range - extension)
        //   transform.position = origin + fwd*(range/2)
        //                      = target - fwd * (range/2 - extension)
        float extension = Mathf.Max(0f, galeDeathBeamGroundExtension);
        Vector3 targetPos = target.transform.position;
        Vector3 lookUp = Mathf.Abs(Vector3.Dot(fwd, Vector3.forward)) > 0.99f ? Vector3.right : Vector3.forward;
        beam.transform.rotation = Quaternion.LookRotation(fwd, lookUp);
        beam.transform.position = targetPos - fwd * (beam.range * 0.5f - extension);
        beam.Init(owner, enemyLayers);
        // Don't track in activeBeam / OnFireDown lockout — sky beams
        // can stack with each other and with the player charging again.
    }

    /// <summary>
    /// Pick a uniformly random alive Enemy whose horizontal distance from
    /// the hero is within radius. Allocation-free two-pass pattern.
    /// </summary>
    private static Enemy PickRandomAliveEnemyAroundHero(Hero owner, float radius)
    {
        var spawner = EnemySpawner.Instance;
        if (spawner == null || spawner.AliveEnemies == null || owner == null) return null;
        var list = spawner.AliveEnemies;
        Vector3 origin = owner.transform.position;
        float r2 = radius * radius;

        int validCount = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e.IsDead) continue;
            Vector3 d = e.transform.position - origin;
            d.y = 0f;
            if (d.sqrMagnitude > r2) continue;
            validCount++;
        }
        if (validCount == 0) return null;

        int chosen = Random.Range(0, validCount);
        int seen = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e.IsDead) continue;
            Vector3 d = e.transform.position - origin;
            d.y = 0f;
            if (d.sqrMagnitude > r2) continue;
            if (seen == chosen) return e;
            seen++;
        }
        return null;
    }

    /// <summary>
    /// Toggle homing on a freshly-spawned arrow if the shot was at or above
    /// homingChargeThreshold. Uses near-perfect turn rate so the arrow can
    /// tightly track a moving target, AND overrides pierceCount so the arrow
    /// keeps chaining through enemies for the whole homing window. Must run
    /// BEFORE Launch — Launch snapshots pierceCount into pierceRemaining.
    /// </summary>
    private void ApplyHomingIfCharged(Projectile p, bool fullyCharged)
    {
        if (p == null || !fullyCharged) return;
        p.homing = true;
        p.homingTurnSpeed              = fullyChargedHomingTurnSpeed;
        p.homingTurnSpeedRampPerSecond = fullyChargedHomingTurnSpeedRamp;
        p.homingTurnSpeedMax           = fullyChargedHomingTurnSpeedMax;
        // Range boost extends the homing window in lockstep with the arrow's
        // lifetime, so the arrow can keep homing for as long as it's alive.
        p.homingDuration  = fullyChargedHomingDuration + Mathf.Max(0f, arrowLifetimeBonus);
        p.homingMaxRange  = fullyChargedHomingMaxRange;
        p.homingRetargetEachFrame = true;
        // Override the prefab's pierce count so the homing arrow doesn't
        // burn out after a fixed number of hits (Arrow_Piercing.prefab ships
        // with pierceCount = 5, which would cap the chain).
        p.pierceCount = fullyChargedPierceCount;
    }

    private void FireDeathBeam(Hero owner)
    {
        Vector3 spawn = owner.transform.position;
        DeathBeam beam = Instantiate(deathBeamPrefab, spawn, owner.transform.rotation);
        // Apply boost-driven bonuses to this instance before it initializes
        // (so its damage tick, visual stretch, and lifetime reflect upgrades).
        if (deathBeamRadiusBonus   > 0f) beam.radius   += deathBeamRadiusBonus;
        if (deathBeamDurationBonus > 0f) beam.duration += deathBeamDurationBonus;
        // Range boost: bonus Max HP%-per-second damage. The field on the beam
        // is in fraction units (0.01 = 1%), but the bow tracks the bonus in
        // percent points (0.5 = 0.5%) for readable inspector values.
        if (deathBeamMaxHpPercentBonus > 0f)
            beam.maxHpFractionPerSecond += deathBeamMaxHpPercentBonus / 100f;

        // Bake the post-overcharge damage multiplier into the beam: every
        // second held past overchargeTime added postOverchargeDamageRate to
        // the multiplier, so dps *= (1 + accumulated) before hero modifiers.
        float dps = beam.damagePerSecond + deathBeamDpsBonus;
        if (postOverchargeAccumulated > 0f) dps *= (1f + postOverchargeAccumulated);

        // Roll hero damage modifiers once per beam so the whole beam tick rate
        // is consistently boosted (or critting) for its full duration.
        beam.damagePerSecond = owner.ComputeAttackDamage(dps);
        beam.Init(owner, enemyLayers);

        // Track the active beam so OnFireDown can lock out new charges until
        // it finishes. Unity's null-equivalence on destroyed GameObjects
        // handles the auto-clear; the beam destroys itself when its duration
        // elapses (DeathBeam.Update -> Destroy(gameObject)).
        activeBeam = beam;
    }
}
