using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Final boss controller. Add this component alongside an <see cref="Enemy"/>
/// component (with <c>behavior = Enemy.Behavior.SlimeGod</c>) on the boss prefab.
/// Enemy.cs handles HP / world bar / death animation; SlimeGod.cs drives the
/// rigidbody, telegraphs, attacks, and damage-reduction curve.
///
/// Phases:
///   * Spawning   - 1 unavoidable telegraphed attack that takes the player to 1 HP.
///   * Melee      - alternates between a 10-dash pattern (each dash adds a shield)
///                  and an 8-bounce AOE pattern.
///   * Ranged     - alternates between an 8-direction rotating arrow spam
///                  (boss chases the player) and 5 telegraphed waves of arrows
///                  fired from the air (first 2 waves from the boss, last 3 from
///                  the edge of the screen).
///
/// Damage reduction lerps from <see cref="startDamageReduction"/> at spawn down
/// to <see cref="endDamageReduction"/> over <see cref="damageReductionDecayTime"/>
/// seconds. Enemy.TakeDamage calls into <see cref="ModifyIncomingDamage"/> so
/// every hit goes through the curve plus the shield-stack absorber.
/// </summary>
[RequireComponent(typeof(Enemy))]
[RequireComponent(typeof(Rigidbody))]
public class SlimeGod : MonoBehaviour
{
    // -------------------- Tunables --------------------

    [Header("Damage Reduction")]
    [Tooltip("Damage reduction at spawn AND throughout the grace period (0..1). 0.999 = essentially invincible.")]
    [Range(0f, 0.999f)] public float startDamageReduction = 0.999f;
    [Tooltip("Damage reduction once the decay finishes (0..1). 0 = no reduction.")]
    [Range(0f, 0.999f)] public float endDamageReduction = 0f;
    [Tooltip("Seconds at the START of the fight where damage reduction stays locked at startDamageReduction (boss can barely be hurt). The lerp toward endDamageReduction only begins AFTER this grace period elapses.")]
    public float damageReductionGracePeriod = 40f;
    [Tooltip("Seconds for damage reduction to lerp from startDamageReduction to endDamageReduction once the grace period ends. Total time from spawn until full damage = grace + decay.")]
    public float damageReductionDecayTime = 120f;

    [Header("Spawn Attack")]
    [Tooltip("Seconds the boss telegraphs before slamming the player to half max HP.")]
    public float spawnTelegraphTime = 2.5f;
    [Tooltip("Aura/light radius shown during the spawn telegraph.")]
    public float spawnTelegraphRadius = 6f;
    [Tooltip("Camera shake amplitude when the spawn attack lands.")]
    public float spawnShakeAmplitude = 0.7f;
    [Tooltip("Camera shake duration when the spawn attack lands.")]
    public float spawnShakeDuration = 0.9f;

    [Header("Spawn Attack — Bonus Aerial Volleys")]
    [Tooltip("If true, after the spawn slam resolves the boss fires a series of standard aerial-style volleys (same look as the AerialPattern wave), one after the other.")]
    public bool spawnGridVolleysEnabled = true;
    [Tooltip("How many bonus aerial volleys are fired in sequence (during the spawn attack AND alongside every Sweeping Beam attack).")]
    public int spawnGridVolleyCount = 3;
    [Tooltip("Arrows per volley — uses the same edge-ring spawn pattern as the main aerial wave.")]
    public int spawnGridArrowsPerVolley = 12;
    [Tooltip("Seconds the per-volley telegraph stays visible before the arrows fly.")]
    public float spawnGridTelegraphTime = 0.9f;
    [Tooltip("Pause between successive bonus volleys.")]
    public float spawnGridIntervalBetween = 0.4f;
    [Tooltip("Travel speed for bonus-volley arrows.")]
    public float spawnGridArrowSpeed = 32f;
    [Tooltip("Damage per bonus-volley arrow.")]
    public float spawnGridArrowDamage = 15f;
    [Tooltip("Visual scale for bonus-volley arrows. Trigger collider stays full size via ApplyArrowScale.")]
    public float spawnGridArrowScale = 0.55f;
    [Tooltip("Edge-spawn ring radius around each volley's predicted target (same role as aerialEdgeSpawnRadius).")]
    public float spawnGridEdgeSpawnRadius = 18f;
    [Tooltip("Per-volley prediction lead time (seconds). Volley i uses (i + 0.5) × this — first volley aims close to the player, later volleys lead progressively further ahead.")]
    public float spawnGridLeadStep = 0.4f;
    [Tooltip("If true, the boss continuously fires single aerial-style volleys at this interval throughout the fight, EXCEPT while the AerialPattern or SweepingBeamPattern is running (since those already include their own volleys).")]
    public bool bonusVolleysContinuousEnabled = true;
    [Tooltip("Seconds between bonus aerial volleys when no main aerial / sweeping-beam pattern is active.")]
    public float bonusVolleyInterval = 10f;

    [Header("Phase Timings")]
    [Tooltip("Pause between sub-patterns (lets the player breathe and read telegraphs).")]
    public float interPatternPause = 1.4f;
    [Tooltip("Once the boss has been alive this many seconds, MeleePattern stacks a parallel ranged-fire side coroutine on top — the boss attacks with both melee patterns AND ranged volleys at the same time. Default 480 = 8 minutes into the boss fight.")]
    public float enragePhaseTime = 480f;
    [Tooltip("Seconds between each spam wave fired by the enraged side coroutine. Lower = denser bullet spam stacked over the dashes/bounces.")]
    public float enragedRangedSpamInterval = 0.18f;

    [Header("Movement")]
    [Tooltip("Default cruising speed in units/sec.")]
    public float baseMoveSpeed = 3f;
    [Tooltip("How far inside the arena edge the boss must stay. Dash overshoots, bounce landings, and aerial drops are all clamped to ±(arenaSize/2 − margin) so the boss can't tunnel through walls or jump off the map.")]
    public float arenaEdgeMargin = 1.5f;
    [Tooltip("The boss's estimated player-velocity (used by dash / bounce / aerial predictions) is capped at player.moveSpeed × this. Keep at 1.0 so a dash spike (~22 u/s) doesn't flatten the prediction off-screen — the prediction only takes the player's natural walk speed into account. Bump above 1 if you DO want the boss to react to dashes.")]
    public float playerSpeedPredictionCap = 1.0f;
    [Tooltip("If a single frame's raw velocity exceeds (cap × this multiplier) — i.e. the player just dashed — the prediction skips that frame entirely instead of letting the dash partially influence the smoothed estimate. 2.0 = anything more than 2× walk speed is treated as a dash and ignored.")]
    public float playerSpeedSpikeMultiplier = 2.0f;

    // ---- Melee: 10-dash pattern ----

    [Header("Melee — Dash Pattern")]
    public int dashCount = 10;
    [Tooltip("How far past the player each dash overshoots, in units.")]
    public float dashOvershoot = 4f;
    [Tooltip("Seconds between consecutive dashes (no telegraph — dashes are quick and back-to-back).")]
    public float dashGap = 0.12f;
    [Tooltip("How long the boss locks on to the player's CURRENT position before each dash starts. The position is snapshotted at the start of this delay and frozen during it, so the player can step out of the line during the delay to dodge.")]
    public float dashLockOnDelay = 0.18f;
    [Tooltip("How long a single dash takes (boss interpolates from start to end over this time).")]
    public float dashDuration = 0.16f;
    [Tooltip("Radius that counts as a hit on the player during the dash.")]
    public float dashHitRadius = 2.2f;
    [Tooltip("Damage dealt when the dash overlaps the player.")]
    public float dashDamage = 30f;
    [Tooltip("Screen-shake amplitude when a dash slams into the player.")]
    public float dashShakeAmplitude = 0.35f;
    [Tooltip("Screen-shake duration when a dash slams into the player.")]
    public float dashShakeDuration = 0.18f;
    [Tooltip("Lighter screen-shake amplitude for every dash, even ones that miss (sells the speed/impact).")]
    public float dashFootstepShakeAmplitude = 0.08f;
    [Tooltip("Lighter screen-shake duration for every dash.")]
    public float dashFootstepShakeDuration = 0.05f;
    [Tooltip("If true, every dash also spits aerial arrows out to its left AND right at evenly-spaced points along the dash path. Uses the aerialArrowPrefab + aerial damage / speed / scale tunables. Set count to 0 to disable.")]
    public bool dashSideArrowsEnabled = true;
    [Tooltip("Number of arrow PAIRS (one left + one right) fired per dash, distributed evenly along the dash duration. 3 → arrows at 25 %, 50 %, 75 % of the dash.")]
    public int dashSideArrowCount = 3;
    [Tooltip("If true, every dash ends with a quick UN-telegraphed death beam swept in front of the boss along the dash direction. Runs in parallel so the next dash isn't delayed.")]
    public bool dashEndBeamEnabled = true;
    [Tooltip("How long the post-dash sweep beam stays alive. Short = the player can dodge by sidestepping; long = a bigger trailing slash.")]
    public float dashEndBeamDuration = 0.18f;
    [Tooltip("Total arc swept by the post-dash beam, in degrees. Sweeps from -half to +half centered on the dash direction.")]
    public float dashEndBeamSweepDegrees = 70f;
    [Tooltip("DPS of the post-dash sweep beam while the player is on its line.")]
    public float dashEndBeamDamagePerSecond = 80f;
    [Tooltip("Hit radius around the post-dash beam line for damage detection.")]
    public float dashEndBeamHitRadius = 1.0f;
    [Tooltip("Visual width (LineRenderer fallback only — the bow visual prefab uses beamFireVisualRadius). Same units as beamFireWidth.")]
    public float dashEndBeamWidth = 0.6f;
    [Tooltip("Color of the post-dash sweep beam fallback line.")]
    public Color dashEndBeamColor = new Color(1f, 0.65f, 0.25f, 1f);
    [Tooltip("If true, every dash drops a trail of red 'burn' patches under the dash path that linger and damage the player while standing on them.")]
    public bool dashBurnEnabled = true;
    [Tooltip("Seconds each burn patch lasts before fading away. Patches DON'T despawn when the boss dies — the trail keeps burning until each patch ages out.")]
    public float dashBurnDuration = 3f;
    [Tooltip("Radius of each burn patch on the ground.")]
    public float dashBurnRadius = 1.5f;
    [Tooltip("Damage-per-second the hero takes while standing on any burn patch. Routed through ScaledDamage.")]
    public float dashBurnDamagePerSecond = 25f;
    [Tooltip("Seconds between burn-patch drops while the boss is mid-dash. With dashDuration ~0.16, an interval of 0.04 lays ~4 overlapping patches per dash for a continuous trail.")]
    public float dashBurnSpawnInterval = 0.04f;
    [Tooltip("Color of the burn-patch disc. Alpha drives how visible the patch is at peak.")]
    public Color dashBurnColor = new Color(1f, 0.18f, 0.12f, 0.85f);

    // ---- Melee: bounce pattern ----

    [Header("Melee — Bounce Pattern")]
    public int bounceCount = 8;
    [Tooltip("Seconds the bounce circle is visible before the boss lands.")]
    public float bounceTelegraphTime = 0.65f;
    [Tooltip("Seconds the boss spends in the air for each bounce.")]
    public float bounceArcTime = 0.7f;
    [Tooltip("Peak height of each bounce arc.")]
    public float bounceArcHeight = 12f;
    [Tooltip("Damage radius around the landing point.")]
    public float bounceImpactRadius = 3.6f;
    [Tooltip("Damage dealt to the player if inside the bounce impact radius on landing.")]
    public float bounceImpactDamage = 25f;
    [Tooltip("How far ahead the bounce predicts the player. Bounces aim slightly ahead of the player's motion.")]
    public float bounceLeadDistance = 2.4f;
    [Tooltip("Screen-shake amplitude when the boss slams down at the end of a bounce.")]
    public float bounceShakeAmplitude = 0.45f;
    [Tooltip("Screen-shake duration when the boss slams down at the end of a bounce.")]
    public float bounceShakeDuration = 0.32f;
    [Tooltip("If true, every bounce landing also fires an expanding ring shockwave that deals delayed AOE damage to the player as the wavefront passes through them.")]
    public bool bounceShockwaveEnabled = true;
    [Tooltip("How far the bounce shockwave expands before fading. Should usually be larger than bounceImpactRadius so the wave continues past the immediate slam.")]
    public float bounceShockwaveMaxRadius = 12f;
    [Tooltip("Seconds the bounce shockwave takes to expand to its max radius. Determines how long the player has to escape before the wavefront catches up.")]
    public float bounceShockwaveDuration = 0.7f;
    [Tooltip("Damage dealt once when the shockwave's expanding ring sweeps over the player's position.")]
    public float bounceShockwaveDamage = 18f;
    [Tooltip("Half-thickness of the damaging ring (in world units). The wave hits when the player is within ±band of the current radius.")]
    public float bounceShockwaveBandWidth = 1.6f;
    [Tooltip("Color of the bounce shockwave ring.")]
    public Color bounceShockwaveColor = new Color(1f, 0.55f, 0.1f, 0.95f);

    // ---- Melee parallel attack: homing barrage ----

    [Header("Melee — Homing Barrage (runs in parallel with dashes / bounces)")]
    [Tooltip("Homing EnemyProjectile prefab spit out in barrages while the boss is dashing or bouncing. Leave null to disable the barrage entirely.")]
    public EnemyProjectile meleeHomingProjectilePrefab;
    [Tooltip("Seconds before the first barrage of a melee pattern (lets the dashes/bounces start cleanly).")]
    public float meleeHomingBarrageStartDelay = 0.5f;
    [Tooltip("Seconds between consecutive homing barrages while the melee pattern is active.")]
    public float meleeHomingBarrageInterval = 1.4f;
    [Tooltip("Number of homing projectiles fired per barrage. They fan out in a ring around the boss with a small random angular jitter.")]
    public int meleeHomingArrowsPerBarrage = 5;
    [Tooltip("Damage of each homing projectile.")]
    public float meleeHomingArrowDamage = 8f;
    [Tooltip("Travel speed of each homing projectile.")]
    public float meleeHomingArrowSpeed = 12f;
    [Tooltip("Degrees/sec the projectile can turn toward the player. Lower = lazy curve, higher = locks on hard.")]
    public float meleeHomingTurnSpeed = 90f;
    [Tooltip("Seconds the projectile actively homes before falling back to a straight line.")]
    public float meleeHomingDuration = 4f;
    [Tooltip("Stops homing if the player is further than this. Lets the projectile fly past instead of chasing forever.")]
    public float meleeHomingMaxRange = 30f;
    [Tooltip("Uniform scale applied to spawned homing projectiles (1 = prefab default; smaller = thinner).")]
    public float meleeHomingArrowScale = 0.6f;
    [Tooltip("Vertical offset above the boss pivot where the homing projectiles spawn.")]
    public float meleeHomingSpawnHeight = 1.2f;
    [Tooltip("Random angular jitter in degrees added to each projectile's direction so the ring doesn't look perfectly geometric.")]
    public float meleeHomingAngleJitter = 12f;

    // ---- Ranged: spam pattern ----

    [Header("Ranged — 8-Direction Spam")]
    public float spamDuration = 7f;
    public int spamArrowsPerWave = 8;
    [Tooltip("Seconds between consecutive 8-arrow waves. Smaller = smoother visible rotation (the angle increments by spamRotationSpeed × interval each wave, so a tiny interval makes the spiral look continuous instead of pulsing).")]
    public float spamWaveInterval = 0.1f;
    [Tooltip("Degrees per second the spam fan rotates.")]
    public float spamRotationSpeed = 50f;
    [Tooltip("Speed the boss chases the player while spamming.")]
    public float spamChaseSpeed = 2.4f;
    [Tooltip("Damage per individual spam arrow. Spam fires every spamWaveInterval seconds with spamArrowsPerWave arrows per wave, so total DPS = damage × arrowsPerWave / interval — keep this number small.")]
    public float spamArrowDamage = 3f;
    public float spamArrowSpeed = 14f;
    [Tooltip("Uniform scale applied to spawned spam arrows. 1 = prefab default; smaller = thinner.")]
    public float spamArrowScale = 0.5f;
    [Tooltip("Vertical offset above the boss pivot where spam arrows spawn / aim. Lower this if your boss prefab's pivot sits in its middle (so arrows would spawn over the player's head). 0.5 reads as chest-height for a typical hero on a boss with a feet-level pivot.")]
    public float spamArrowSpawnHeight = 0.5f;

    // ---- Ranged: aerial waves ----

    [Header("Ranged — 5 Aerial Waves")]
    public int aerialWaveCount = 5;
    [Tooltip("Height the boss rises to during the aerial pattern.")]
    public float aerialFlyUpHeight = 18f;
    [Tooltip("Seconds the boss takes to rise up.")]
    public float aerialFlyUpTime = 0.45f;
    [Tooltip("Seconds between each wave's resolution. Includes the telegraph time.")]
    public float aerialWaveInterval = 0.85f;
    [Tooltip("Seconds each wave's telegraph is visible before the arrows launch.")]
    public float aerialTelegraphTime = 0.45f;
    public int aerialArrowsPerWave = 12;
    public float aerialArrowDamage = 18f;
    public float aerialArrowSpeed = 38f;
    [Tooltip("Uniform scale applied to spawned aerial arrow projectiles. 1 = prefab default; smaller = thinner, harder-to-see arrows.")]
    public float aerialArrowScale = 0.55f;
    [Tooltip("Radius of the ground ring drawn for each aerial wave's telegraph.")]
    public float aerialRingRadius = 10f;
    [Tooltip("Wave indices >= this come from the edge of the screen instead of the boss. Defaults to 2 (waves 0 & 1 from boss, waves 2/3/4 from edges).")]
    public int aerialEdgeWaveStartIndex = 2;
    [Tooltip("Distance from the player along each cardinal/intermediate direction where edge-spawned arrows originate.")]
    public float aerialEdgeSpawnRadius = 22f;
    [Tooltip("Height above ground where aerial arrows ACTUALLY spawn (so they fly horizontally toward the player instead of plunging from the boss). Keep low (~1.0) for ground-level horizontal volleys.")]
    public float aerialArrowSpawnHeight = 1.0f;
    [Tooltip("Seconds of player-velocity prediction per-wave bias. Wave i lead = (i - waveCount/2) × this. Lower waves lag, higher waves lead.")]
    public float aerialLeadStep = 0.45f;
    [Tooltip("Radius around the player's CURRENT position used by random-target waves (every other wave). Larger = more chaotic spread, smaller = closer to a sniper shot.")]
    public float aerialRandomTargetRadius = 4f;
    [Tooltip("How far past the arrow's target point the telegraph line is drawn. Without this the line ends at the target (mid-arena), which reads visually as 'this is where the arrow stops'. Extending it sells the actual flight path through the target and out the other side.")]
    public float aerialTelegraphExtensionDistance = 40f;
    [Tooltip("Width of the aerial-volley telegraph lines specifically. Lower than the shared telegraphLineWidth so the dense fan of arrow lines doesn't visually overwhelm the screen.")]
    public float aerialTelegraphLineWidth = 0.07f;
    [Tooltip("Vertical offset added to the aerial-arrow telegraph lines (visual only — arrow spawn / flight y is unchanged). Negative drops the lines closer to the ground so they read as floor markings instead of mid-air streaks.")]
    public float aerialTelegraphYOffset = -0.3f;
    [Tooltip("If true, each aerial wave checks how far the player has run from the attack center; if they've fled past aerialEscapeRadius, an EXTRA tracking volley fires that predicts at multiple lead scales so the player can't outrun the whole pattern.")]
    public bool aerialEscapePunishEnabled = true;
    [Tooltip("Distance from the boss's takeoff position the player has to leave for the per-wave punishment volley to fire. Below this distance the wave runs as normal with no punishment.")]
    public float aerialEscapeRadius = 12f;
    [Tooltip("Number of arrows in the punishment volley. Each one predicts the player's position at a different lead time spread across the min/max range, so the volley fans out along the player's escape vector.")]
    public int aerialPunishArrowCount = 6;
    [Tooltip("Smallest prediction lead used by the punishment volley (seconds). Negative leads aim at where the player WAS, 0 aims at NOW.")]
    public float aerialPunishMinLead = 0f;
    [Tooltip("Largest prediction lead used by the punishment volley (seconds). With the player's velocity capped by playerSpeedPredictionCap, a 1.2s lead means roughly 6 units in front of them.")]
    public float aerialPunishMaxLead = 1.2f;
    [Tooltip("Seconds the punishment-volley telegraph is visible before the arrows fire.")]
    public float aerialPunishTelegraphTime = 0.4f;
    [Tooltip("Damage per punishment-volley arrow.")]
    public float aerialPunishArrowDamage = 18f;
    [Tooltip("Travel speed for punishment-volley arrows.")]
    public float aerialPunishArrowSpeed = 38f;
    [Tooltip("Scale applied to punishment-volley arrows (visual). Trigger collider stays full-size via ApplyArrowScale.")]
    public float aerialPunishArrowScale = 0.55f;
    [Tooltip("Edge-spawn ring radius around each punishment-volley target. The arrows come from points around the predicted spot at this distance.")]
    public float aerialPunishEdgeSpawnRadius = 16f;
    [Tooltip("Total arc (degrees) the punishment volley fans across, centered on the player's velocity direction. With 120° you get arrows spread from 60° left of the escape vector to 60° right of it — they appear IN FRONT of the player along their escape path. 360 = full ring around the player.")]
    public float aerialPunishFrontArcDegrees = 120f;
    [Tooltip("Smoothing time-constant for the punish-volley telegraph tracking. Higher = telegraph slides more lazily toward the new target each frame; lower = snappy. 0 disables smoothing.")]
    public float aerialPunishTrackSmoothing = 0.15f;
    [Tooltip("LineRenderer sortingOrder used by the punish-volley telegraphs so they always render ON TOP of the regular aerial wave telegraphs. Higher = drawn later (on top). Default 5 is enough to layer above the standard telegraphs (sortingOrder 0).")]
    public int aerialPunishTelegraphSortingOrder = 5;

    // ---- Projectile prefabs ----

    [Header("Projectile Prefabs")]
    [Tooltip("Arrow prefab fired during the 8-direction spam.")]
    public EnemyProjectile spamArrowPrefab;
    [Tooltip("Arrow prefab fired by aerial telegraph waves.")]
    public EnemyProjectile aerialArrowPrefab;

    // ---- Visuals ----

    [Header("Visuals")]
    public float telegraphLineWidth = 0.18f;
    public Color dashTelegraphColor   = new Color(1f, 0.15f, 0.15f);
    public Color bounceTelegraphColor = new Color(1f, 0.55f, 0.1f);
    public Color spawnTelegraphColor  = new Color(1f, 0.05f, 0.05f);
    public Color shieldColor          = new Color(0.4f, 0.7f, 1f);
    public Color aerialTelegraphColor = new Color(0.9f, 0.2f, 0.6f, 120f / 255f);

    // ---- Constant attack: Death Beam ----

    [Header("Death Beam (constant attack)")]
    [Tooltip("Seconds between each death-beam cycle. The cycle itself is fadeIn + lock + fire, so the actual gap between fires is cooldown + fadeIn + lock + fire.")]
    public float beamCooldown = 3f;
    [Tooltip("Seconds the telegraph fades in (alpha 0 → 1) while tracking the player.")]
    public float beamFadeInTime = 1.6f;
    [Tooltip("Once the telegraph is fully opaque (alpha = 1), how long it stays locked on the last predicted spot before firing. This is the dodge window — longer = easier to dodge.")]
    public float beamLockTime = 2.0f;
    [Tooltip("How long the actual death beam is alive once it fires. The beam slowly rotates to follow the player during this whole window.")]
    public float beamFireDuration = 1.0f;
    [Tooltip("Damage per second dealt while the player is within beamHitRadius of the firing beam's line.")]
    public float beamDamagePerSecond = 90f;
    [Tooltip("Hit radius around the beam line (units). Anything within this from the line counts as on-beam.")]
    public float beamHitRadius = 1.2f;
    [Tooltip("Maximum distance the beam reaches in world units.")]
    public float beamRange = 30f;
    [Tooltip("Width of the telegraph line (the thin tracking line).")]
    public float beamTelegraphWidth = 0.15f;
    [Tooltip("Width of the actual death beam when it fires (used by the LineRenderer fallback if no Visual Prefab is assigned).")]
    public float beamFireWidth = 0.55f;
    [Tooltip("Degrees per second the firing beam rotates toward the player. Lower = easier to dodge, higher = harder to outrun. ~30 lets a sidestepping hero break the beam quickly.")]
    public float beamFireTrackDegPerSec = 30f;
    [Tooltip("If the angle between the beam's current direction and the player is at least this many degrees, the rotation rate is multiplied by Beam Fast Track Multiplier. Lets the beam snap back when the player gets very far off-axis (e.g. dashes behind the boss) but stay slow/dodgeable when on-axis.")]
    public float beamFastTrackThresholdDegrees = 30f;
    [Tooltip("Multiplier applied to the beam's tracking rotation rate when the angle to the player exceeds Beam Fast Track Threshold Degrees. Used by both the constant Death Beam and the Sweeping Death Beam.")]
    public float beamFastTrackMultiplier = 5f;
    [Tooltip("Seconds between damage ticks while the player is on the firing beam.")]
    public float beamDamageTickInterval = 0.1f;
    [Tooltip("Color of the telegraph line. Alpha is driven by the fade-in animation.")]
    public Color beamTelegraphColor = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Color of the actual death beam when it fires (typically brighter / hotter). Used by the LineRenderer fallback.")]
    public Color beamFireColor = new Color(1f, 0.85f, 0.4f);
    [Tooltip("Vertical offset above the boss pivot the beam emits from (its 'mouth/eye').")]
    public float beamOriginHeight = 1.6f;
    [Tooltip("Vertical offset above the player pivot the beam aims at (chest = ~0.8f for typical hero models).")]
    public float beamTargetHeight = 0.8f;
    [Tooltip("Screen-shake amplitude when the death beam fires.")]
    public float beamShakeAmplitude = 0.55f;
    [Tooltip("Screen-shake duration when the death beam fires.")]
    public float beamShakeDuration = 0.35f;
    [Tooltip("OPTIONAL visual prefab for the firing beam (assign Assets/Prefabs/DeathBeamVisual.prefab). The prefab is scaled to (radius*2, radius*2, range) and positioned in front of the boss along its current beam direction. Leave null to use the LineRenderer fallback.")]
    public GameObject deathBeamVisualPrefab;
    [Tooltip("Visual radius of the firing beam (used when Death Beam Visual Prefab is assigned). Half the box's X/Y scale.")]
    public float beamFireVisualRadius = 0.55f;
    [Tooltip("OPTIONAL material that replaces the spawned beam visual's material at runtime. The bow's death beam SHARES the same prefab, so editing the prefab's material would change the bow too — instead, assign a separate material here (e.g. Assets/Materials/DeathBeamGlowBoss.mat) and the boss-spawned visuals will swap to it on Instantiate while the bow stays on the original. Leave null to use whatever material the prefab ships with.")]
    public Material bossDeathBeamMaterial;

    [Header("Beam Lightning Aura (optional)")]
    [Tooltip("OPTIONAL aura prefab spawned in a chain along the firing beam's length so the beam reads as electric. Designed for Hovl Studio's 'Lightning aura' or any character-aura-style prefab. Spawns multiple copies — see Beam Aura Count. Applied to ALL three boss beam types (constant DeathBeam, SweepingBeam, dash-end beam).")]
    public GameObject beamLightningAuraPrefab;
    [Tooltip("Number of aura copies spawned along the beam's length. Includes both endpoints, so 2 = origin + endpoint, 5 = origin + 3 evenly-spaced midpoints + endpoint. Higher = more visual density but more particles to simulate.")]
    public int beamLightningAuraCount = 5;
    [Tooltip("Uniform scale applied to spawned aura prefabs. Most character-aura prefabs were authored at scale 1 for a player-sized character; tune this to match the beam's visual width / desired punch.")]
    public float beamLightningAuraScale = 0.7f;

    // ---- Ranged sub-pattern: Sweeping Death Beam ----

    [Header("Ranged — Sweeping Death Beam (opening attack + ranged sub-pattern)")]
    [Tooltip("Seconds the wide telegraph fades in (alpha 0 → 1) while tracking the player. The boss stands still during this and the lock + fire phases.")]
    public float sweepingBeamTelegraphTime = 1.5f;
    [Tooltip("Seconds the telegraph stays locked at alpha 1 with a frozen direction after the fade-in. This is the dodge window before the giant beam fires.")]
    public float sweepingBeamLockTime = 1.5f;
    [Tooltip("Seconds the actual giant beam is alive after the telegraph. The beam slowly rotates to follow the player during this.")]
    public float sweepingBeamFireDuration = 2.6f;
    [Tooltip("Width of the telegraph during fade-in.")]
    public float sweepingBeamTelegraphWidth = 1.4f;
    [Tooltip("Width of the actual giant beam during the fire phase.")]
    public float sweepingBeamFireWidth = 2.6f;
    [Tooltip("Maximum distance the sweeping beam reaches.")]
    public float sweepingBeamRange = 40f;
    [Tooltip("Base degrees per second the beam rotates toward the player during the fire phase. Scaled at runtime by player distance (see Close/Far multipliers).")]
    public float sweepingBeamTrackDegPerSec = 25f;
    [Tooltip("Player distance at or below which the sweeping beam tracks at its CLOSE multiplier (barely moves). Stay inside this radius and the beam can't keep up with sidestepping.")]
    public float sweepingBeamCloseDistance = 6f;
    [Tooltip("Player distance at or above which the sweeping beam tracks at its FAR multiplier (locks on aggressively). Hide behind cover or stand far back and the beam catches you fast.")]
    public float sweepingBeamFarDistance = 25f;
    [Tooltip("Multiplier on Sweeping Beam Track Deg Per Sec when the player is at or closer than Close Distance. <1 = the beam barely tracks; near 0 = practically frozen.")]
    public float sweepingBeamCloseTrackMultiplier = 0.15f;
    [Tooltip("Multiplier on Sweeping Beam Track Deg Per Sec when the player is at or farther than Far Distance. >1 = aggressive long-range tracking.")]
    public float sweepingBeamFarTrackMultiplier = 4f;
    [Tooltip("Damage per second dealt while the player is within sweepingBeamHitRadius of the beam line.")]
    public float sweepingBeamDamagePerSecond = 60f;
    [Tooltip("Hit radius around the beam line for damage. Wider than the regular death beam since this one's the giant variant.")]
    public float sweepingBeamHitRadius = 1.6f;
    [Tooltip("Color of the sweeping-beam telegraph. Alpha is driven by the fade-in animation.")]
    public Color sweepingBeamTelegraphColor = new Color(1f, 0.2f, 0.2f);
    [Tooltip("Color of the actual giant beam during the fire phase.")]
    public Color sweepingBeamFireColor = new Color(1f, 0.85f, 0.4f);
    [Tooltip("Screen-shake amplitude when the sweeping beam fires.")]
    public float sweepingBeamShakeAmplitude = 0.7f;
    [Tooltip("Screen-shake duration when the sweeping beam fires.")]
    public float sweepingBeamShakeDuration = 0.45f;
    [Tooltip("Seconds between damage ticks while the player is on the sweeping beam (smaller = smoother but more TakeDamage calls).")]
    public float sweepingBeamDamageTickInterval = 0.1f;

    // ---- Rage Phase: periodic empower channel ----

    [Header("Rage Phase (periodic empower channel)")]
    [Tooltip("If true, the boss periodically pauses and channels an orange empower that boosts its damage taken AND damage dealt while adding extra projectiles. Lets a low-DPS player end the fight by trading toughness for lethality.")]
    public bool ragePhaseEnabled = true;
    [Tooltip("Seconds of boss life before the FIRST rage phase triggers. After this, the phase repeats every Rage Phase Interval seconds.")]
    public float ragePhaseFirstAt = 120f;
    [Tooltip("Seconds between subsequent rage phases after the first one.")]
    public float ragePhaseInterval = 60f;
    [Tooltip("Seconds the boss spends frozen, gathering orange particles before the multipliers apply. Player gets a free attack window — that's the trade.")]
    public float ragePhaseChannelDuration = 1.6f;
    [Tooltip("Multiplier applied to incoming damage at each rage trigger. 3 = damage taken triples each phase (stacks exponentially: 3x, 9x, 27x …).")]
    public float ragePhaseDamageTakenMultiplier = 3f;
    [Tooltip("Multiplier applied to outgoing damage at each rage trigger. 2 = boss damage doubles each phase (stacks exponentially with the per-spawn damage scaling).")]
    public float ragePhaseDamageDealtMultiplier = 2f;
    [Tooltip("Extra projectiles added to every multi-projectile attack at each rage trigger. Stacks ADDITIVELY across phases (2 → 4 → 6 …).")]
    public int ragePhaseExtraProjectilesPerTrigger = 2;
    [Tooltip("Color of the gathering particles + ground ring during the channel.")]
    public Color ragePhaseColor = new Color(1f, 0.55f, 0.1f, 1f);
    [Tooltip("Radius of the orange telegraph ring drawn under the boss during the channel.")]
    public float ragePhaseTelegraphRadius = 6f;
    [Tooltip("Radius from the boss at which gathering particles spawn (they fly inward toward the boss center).")]
    public float ragePhaseGatherRadius = 7f;
    [Tooltip("Particles spawned per gathering tick.")]
    public int ragePhaseGatherParticlesPerBurst = 4;
    [Tooltip("Seconds between each gather-burst spawn during the channel.")]
    public float ragePhaseGatherSpawnInterval = 0.05f;
    [Tooltip("Speed of each gathering particle as it flies inward toward the boss.")]
    public float ragePhaseGatherParticleSpeed = 12f;
    [Tooltip("Lifetime (seconds) of each gathering particle.")]
    public float ragePhaseGatherParticleLifetime = 0.55f;
    [Tooltip("Visual size of each gathering particle.")]
    public float ragePhaseGatherParticleSize = 0.18f;
    [Tooltip("Camera-shake amplitude when the rage phase resolves and the multipliers apply.")]
    public float ragePhaseShakeAmplitude = 0.4f;
    [Tooltip("Camera-shake duration when the rage phase resolves.")]
    public float ragePhaseShakeDuration = 0.3f;
    [Tooltip("Peak amplitude of the rolling channel shake (the rumble while the boss is gathering particles). Builds from ~0 at channel start up to this at the climax.")]
    public float ragePhaseChannelShakeAmplitude = 0.18f;
    [Tooltip("Seconds between each tick of channel-shake re-trigger. Shorter = denser rumble.")]
    public float ragePhaseChannelShakeInterval = 0.1f;
    [Tooltip("Radius of the burst shockwave drawn when the rage phase resolves.")]
    public float ragePhaseBurstRadius = 16f;
    [Tooltip("Duration of the burst shockwave drawn when the rage phase resolves.")]
    public float ragePhaseBurstDuration = 0.55f;
    [Tooltip("Seconds of boss life before the boss bar starts displaying a red 'SUDDEN DEATH' tag. Purely cosmetic — signals to the player that the fight has gone on much longer than intended.")]
    public float suddenDeathAt = 480f;
    /// <summary>True once boss life has exceeded <see cref="suddenDeathAt"/> seconds — drives the red SUDDEN DEATH label on the boss bar.</summary>
    public bool IsSuddenDeath => IsActive && lifeTimer >= Mathf.Max(0f, suddenDeathAt);

    // -------------------- Runtime state --------------------

    // ---- Per-spawn stat multipliers (set by EnemySpawner just after Instantiate) ----
    // These scale linearly with finalBossesSpawned and stack on top of the
    // exponential HP scaling. They're NonSerialized so the prefab values stay
    // 1× and the spawner is the only thing that changes them at runtime.

    [System.NonSerialized] public float attackDamageMultiplier   = 1f;
    [System.NonSerialized] public float projectileCountMultiplier = 1f;
    [System.NonSerialized] public float attackSpeedMultiplier    = 1f;

    /// <summary>
    /// Skip the EnemyProjectile.visualPrefab override on every projectile this
    /// boss instance spawns. Set by EnemySpawner on respawns so the heavy VFX
    /// trails (FireIce / Gabriel Aguiar) only render on the first summon —
    /// projectile counts scale up with each respawn, so trails are the first
    /// thing to drop a frame budget at high counts.
    /// </summary>
    [System.NonSerialized] public bool suppressProjectileVisuals = false;

    // Rage-phase runtime state. incomingDamageMultiplier doubles each trigger
    // and is consumed by ModifyIncomingDamage. extraProjectileCount is added
    // by ScaledCount on top of the per-spawn projectile scaling.
    [System.NonSerialized] public float incomingDamageMultiplier = 1f;
    [System.NonSerialized] public int   extraProjectileCount     = 0;
    [System.NonSerialized] public int   ragePhaseTriggerCount    = 0;

    /// <summary>Multiplies a base damage by the per-spawn damage multiplier.</summary>
    private float ScaledDamage(float baseDamage) => baseDamage * Mathf.Max(0f, attackDamageMultiplier);

    /// <summary>
    /// Rounds a base projectile count by the per-spawn projectile multiplier
    /// and then adds the additive rage-phase boost. Always returns at least 1
    /// so a degenerate multiplier doesn't silently turn off an attack.
    /// </summary>
    private int ScaledCount(int baseCount)
        => Mathf.Max(1, Mathf.RoundToInt(baseCount * Mathf.Max(0.01f, projectileCountMultiplier))
                       + Mathf.Max(0, extraProjectileCount));

    /// <summary>
    /// Returns a base interval / cooldown shortened by the per-spawn attack
    /// speed multiplier. attackSpeedMultiplier = 2 ⇒ intervals halved ⇒
    /// twice as many attacks per second.
    /// </summary>
    private float ScaledInterval(float baseInterval)
        => baseInterval / Mathf.Max(0.01f, attackSpeedMultiplier);

    /// <summary>
    /// Spawn an EnemyProjectile, honoring suppressProjectileVisuals. All boss
    /// projectile instantiates go through here so visual-trail overrides can be
    /// gated centrally — flip the bool on this instance and every fresh
    /// projectile skips its visualPrefab setup.
    /// </summary>
    private EnemyProjectile SpawnProjectile(EnemyProjectile prefab, Vector3 position, Quaternion rotation)
    {
        if (suppressProjectileVisuals)
            return EnemyProjectile.InstantiateNoVisual(prefab, position, rotation);
        return Instantiate(prefab, position, rotation);
    }

    /// <summary>
    /// Disable colliders on a freshly-spawned beam visual and apply the
    /// "stretched cube/sphere along Z" scale convention used by
    /// DeathBeamVisual.prefab.
    ///
    /// Also applies the bossDeathBeamMaterial override if assigned — the bow
    /// shares this prefab, so we swap materials per-instance at spawn rather
    /// than mutating the shared prefab.
    /// </summary>
    private void SetupBeamVisualScale(GameObject beamVisual, float radius, float range)
    {
        if (beamVisual == null) return;
        // Always disable colliders — beam visuals are cosmetic.
        foreach (var col in beamVisual.GetComponentsInChildren<Collider>()) col.enabled = false;
        beamVisual.transform.localScale = new Vector3(radius, radius, range);
        // Material override — boss spawns swap to its own material so the bow's
        // copies of this prefab (which keep using the prefab default) stay
        // visually distinct.
        if (bossDeathBeamMaterial != null)
        {
            foreach (var r in beamVisual.GetComponentsInChildren<Renderer>(true))
            {
                // sharedMaterials avoids the per-instance material allocation
                // that .materials = ... triggers; we replace ALL slots with
                // the override so multi-material renderers tint uniformly.
                Material[] sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++) sm[i] = bossDeathBeamMaterial;
                r.sharedMaterials = sm;
            }
        }
    }

    /// <summary>
    /// Position + orient a beam visual along <paramref name="dir"/>.
    /// Centered halfway down the range with local +Z = dir, matching
    /// DeathBeamVisual.prefab's stretched-along-Z mesh convention.
    /// </summary>
    private static void OrientBeamVisual(GameObject beamVisual, Vector3 origin, Vector3 dir, float range)
    {
        if (beamVisual == null) return;
        beamVisual.transform.position = origin + dir * (range * 0.5f);
        beamVisual.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    /// <summary>
    /// Spawn <see cref="beamLightningAuraCount"/> Lightning aura instances
    /// evenly along the beam from origin to (origin + dir * range), so the
    /// beam reads as electric. Each instance is registered into
    /// <see cref="activeBeamAuras"/> for per-frame placement and end-of-fire
    /// (or rage) cleanup. No-op if no prefab is assigned or count &lt; 1.
    /// </summary>
    private void SpawnBeamAuras(Vector3 origin, Vector3 dir, float range)
    {
        if (beamLightningAuraPrefab == null || beamLightningAuraCount < 1) return;
        for (int i = 0; i < beamLightningAuraCount; i++)
        {
            GameObject aura = Instantiate(beamLightningAuraPrefab);
            aura.transform.localScale = Vector3.one * Mathf.Max(0.0001f, beamLightningAuraScale);
            // Disable any colliders so the aura is purely cosmetic.
            foreach (var c in aura.GetComponentsInChildren<Collider>()) c.enabled = false;
            activeBeamAuras.Add(aura);
        }
        UpdateBeamAuras(origin, dir, range);
    }

    /// <summary>
    /// Per-frame: re-position every aura along the beam from origin to the
    /// far endpoint. Auras are spaced uniformly (t=0 → origin, t=1 → endpoint),
    /// so a count of 5 puts auras at 0%, 25%, 50%, 75%, 100% of the beam length.
    /// </summary>
    private void UpdateBeamAuras(Vector3 origin, Vector3 dir, float range)
    {
        int n = activeBeamAuras.Count;
        if (n == 0) return;
        for (int i = 0; i < n; i++)
        {
            var aura = activeBeamAuras[i];
            if (aura == null) continue;
            float t = (n == 1) ? 0.5f : (float)i / (n - 1);
            aura.transform.position = origin + dir * (range * t);
        }
    }

    /// <summary>
    /// Destroy every active beam-aura instance and clear the tracker list.
    /// Called from the normal end-of-fire path and from TriggerRagePhase /
    /// OnBossKilled so an interrupted beam doesn't leave auras floating.
    /// </summary>
    private void ClearBeamAuras()
    {
        for (int i = activeBeamAuras.Count - 1; i >= 0; i--)
        {
            var v = activeBeamAuras[i];
            if (v != null) Destroy(v);
        }
        activeBeamAuras.Clear();
    }

    /// <summary>True from the moment <see cref="Begin"/> runs until the boss dies or the scene unloads.</summary>
    public bool IsActive { get; private set; }
    /// <summary>The signed-up-to-date damage reduction multiplier currently in effect.</summary>
    public float CurrentDamageReduction => damageReduction;
    /// <summary>How long this boss has been alive.</summary>
    public float LifeTime => lifeTimer;
    /// <summary>True after the spawn attack has finished telegraphing and resolved.</summary>
    public bool SpawnAttackResolved { get; private set; }
    /// <summary>How many shield charges the boss currently has stacked from melee dashes.</summary>
    public int ShieldStacks => shieldStacks;

    private Enemy enemy;
    private Rigidbody rb;
    private Hero player;
    private float lifeTimer;
    private float damageReduction;
    private int shieldStacks;
    private GameObject shieldVisual;

    // Player velocity estimate (Hero uses MovePosition so rb.velocity isn't useful).
    private Vector3 prevPlayerPos;
    private Vector3 estPlayerVel;

    private bool meleeNextIsBounce;     // alternates within Melee mode
    private int  rangedSubpatternIndex; // cycles through Spam → Aerial → Sweeping
    private float spamGroundY;          // world y for spam-arrow spawn (snapshot at SpamPattern start)
    private float bossGroundY;          // world y the boss spawned on — used for bonus volleys so arrows don't follow the boss into the air mid-bounce
    private float homeY;                // boss's ground-level y captured at Start (used to drop the boss back to the ground if a rage phase interrupts an aerial pattern mid-air)

    private readonly List<LineRenderer> dashTelegraphPool = new List<LineRenderer>();
    private readonly List<GameObject>   bounceTelegraphPool = new List<GameObject>();
    // Catch-all list for every "BossTelegraph" line spawned by SpawnTelegraphLine
    // (currently the aerial-volley telegraph fan). Local lifecycles in
    // AerialPattern still destroy each line on their normal path; this pool
    // exists so an interrupted pattern (e.g. cancelled by a rage phase) can
    // ClearAllTelegraphs() and not leak floating world-space lines.
    private readonly List<LineRenderer> transientTelegraphPool = new List<LineRenderer>();

    // Catch-all list for every dynamically-instantiated beam-visual GameObject
    // (deathBeamVisualPrefab clones used by DeathBeamRoutine, SweepingBeamPattern,
    // and DashEndBeam — plus the LineRenderer GameObjects those beams fall back
    // to). Each spawn site registers itself here and removes itself on its own
    // normal-path destroy, but if rage phase StopCoroutine's mid-fire the
    // local references are lost — without this pool the visuals would stay
    // floating in the scene for the rest of the fight.
    private readonly List<GameObject> activeBeamVisuals = new List<GameObject>();

    // Lightning auras chained along whichever beam is currently firing.
    // Lifecycle is the same as activeBeamVisuals — spawned at fire start,
    // updated per-frame to track the beam, destroyed at fire end / rage cancel.
    private readonly List<GameObject> activeBeamAuras = new List<GameObject>();

    private Coroutine masterRoutine;
    private Coroutine beamRoutine;
    private Coroutine rageWatcherRoutine;
    private Coroutine bonusVolleyRoutine;
    private bool rageActive;       // true while the channel is in progress
    private float nextRagePhaseTime; // lifeTimer threshold for the next rage trigger
    private bool isAerialOrSweepingActive; // suppresses bonus volley coroutine while these patterns run
    private LineRenderer beamLine;
    private bool dying;
    private ArenaGenerator cachedArena;

    // -------------------- Lifecycle --------------------

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        rb = GetComponent<Rigidbody>();
        damageReduction = startDamageReduction;

        // Snapshot the spawn y as the boss's "ground" reference. Used by the
        // rage phase to pull the boss back down if it was mid-air (in the
        // aerial pattern's fly-up or wave loop) when the rage triggered.
        homeY = transform.position.y;

        // The world-space bar above the boss is replaced by a screen-bottom bar
        // built in GameHUD, so suppress it here (set BEFORE Enemy.Awake is too
        // late — the SlimeGod prefab should also have showHealthBar=false).
        if (enemy != null) enemy.showHealthBar = false;
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.NotifyFinalBossSpawned(this);
    }

    private void Start()
    {
        if (player == null) player = Hero.Instance;
        if (player != null) prevPlayerPos = player.transform.position;
        Begin();
    }

    public void Begin()
    {
        if (IsActive) return;
        IsActive = true;
        // Snapshot the spawn-time ground y. Bounces & aerial fly-ups change
        // transform.position.y at runtime, but bonus volleys want a stable
        // ground reference so arrows don't materialize 12 units in the sky
        // when the boss happens to be mid-bounce when a volley fires.
        bossGroundY = transform.position.y;
        masterRoutine = StartCoroutine(BossLifecycle());
        beamRoutine   = StartCoroutine(DeathBeamRoutine());
        bonusVolleyRoutine = StartCoroutine(BonusAerialVolleysRoutine());

        // Periodic rage-phase watcher. Runs in parallel with the master state
        // machine; when its threshold is hit it stops & restarts master/beam.
        nextRagePhaseTime = Mathf.Max(0f, ragePhaseFirstAt);
        if (ragePhaseEnabled)
            rageWatcherRoutine = StartCoroutine(RagePhaseWatcher());

        // Swap arena music for the boss track. Safe if no MusicManager exists.
        if (MusicManager.Instance != null) MusicManager.Instance.PlayBoss();
    }

    private void Update()
    {
        if (!IsActive) return;

        lifeTimer += Time.deltaTime;

        // Damage reduction curve:
        //   * Hold at startDamageReduction for damageReductionGracePeriod seconds
        //     (99.9% by default — the boss can barely be hurt during this window).
        //   * After the grace period, lerp from startDamageReduction down to
        //     endDamageReduction over damageReductionDecayTime seconds.
        if (damageReductionGracePeriod > 0f && lifeTimer < damageReductionGracePeriod)
        {
            damageReduction = startDamageReduction;
        }
        else if (damageReductionDecayTime > 0.01f)
        {
            float decayT = Mathf.Clamp01((lifeTimer - damageReductionGracePeriod) / damageReductionDecayTime);
            damageReduction = Mathf.Lerp(startDamageReduction, endDamageReduction, decayT);
        }
        else
        {
            damageReduction = endDamageReduction;
        }

        // Player velocity estimate from frame-to-frame deltas.
        //
        // Three layers of protection so the prediction never aims wildly:
        //   1. SPIKE detection: a single frame's raw velocity over
        //      (cap × playerSpeedSpikeMultiplier) means the player almost
        //      certainly dashed. Skip the update entirely so the dash
        //      direction doesn't pollute the smoothed estimate at all.
        //   2. CAP on the raw value before smoothing.
        //   3. CAP on the smoothed value after Lerp — defensive, ensures
        //      estPlayerVel never exceeds capSpeed regardless of how the
        //      smoothing math composes.
        if (player != null)
        {
            Vector3 cur = player.transform.position;
            Vector3 d = cur - prevPlayerPos;
            d.y = 0f;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector3 v = d / dt;

            float capSpeed = Mathf.Max(0.01f, player.moveSpeed) * Mathf.Max(0.01f, playerSpeedPredictionCap);
            float capSq = capSpeed * capSpeed;
            float spikeCap = capSpeed * Mathf.Max(1f, playerSpeedSpikeMultiplier);
            float spikeSq = spikeCap * spikeCap;

            if (v.sqrMagnitude > spikeSq)
            {
                // Dash-magnitude spike — discard this frame's contribution
                // entirely. estPlayerVel keeps its previous walking-speed
                // estimate, so predictions track where the player WAS
                // walking before the dash.
            }
            else
            {
                if (v.sqrMagnitude > capSq) v = v.normalized * capSpeed;
                estPlayerVel = Vector3.Lerp(estPlayerVel, v, 0.4f);
            }

            // Defensive clamp on the smoothed value — guarantees the
            // prediction never overshoots the cap regardless of edge cases
            // (Time.timeScale glitches, mid-frame moveSpeed boosts, etc.).
            if (estPlayerVel.sqrMagnitude > capSq)
                estPlayerVel = estPlayerVel.normalized * capSpeed;

            prevPlayerPos = cur;
        }
    }

    // -------------------- External hooks (Enemy.cs) --------------------

    /// <summary>
    /// Apply the boss's damage-reduction multiplier and shield stacks. Returns
    /// the actual damage Enemy.cs should subtract from currentHP. If the shield
    /// absorbs the hit, returns 0 and consumes a stack.
    /// </summary>
    public float ModifyIncomingDamage(float incoming)
    {
        if (!IsActive) return incoming;

        if (shieldStacks > 0)
        {
            shieldStacks--;
            HitParticles.EmitBurst(transform.position + Vector3.up,
                Vector3.up,
                count: 10, speed: 5f, lifetime: 0.4f, size: 0.13f,
                color: shieldColor, spreadAngle: 60f, useGravity: false);
            if (shieldStacks <= 0) DestroyShieldVisual();
            return 0f;
        }

        // Rage phase scales damage TAKEN exponentially (×2 per trigger by
        // default). We multiply BEFORE the damage-reduction curve so the
        // curve still works as a percentage of the (now amplified) hit.
        float amplified = incoming * Mathf.Max(0f, incomingDamageMultiplier);
        float reduced = amplified * (1f - Mathf.Clamp01(damageReduction));
        return Mathf.Max(0f, reduced);
    }

    /// <summary>Called by Enemy.Die when the boss runs out of HP.</summary>
    public void OnBossKilled()
    {
        if (dying) return;
        dying = true;
        IsActive = false;
        if (masterRoutine != null) StopCoroutine(masterRoutine);
        if (beamRoutine   != null) StopCoroutine(beamRoutine);
        if (rageWatcherRoutine != null) StopCoroutine(rageWatcherRoutine);
        if (bonusVolleyRoutine != null) { StopCoroutine(bonusVolleyRoutine); bonusVolleyRoutine = null; }
        if (beamLine != null) { Destroy(beamLine.gameObject); beamLine = null; }
        // Same risk on death as on rage — local beam-visual refs vanish when
        // coroutines stop, so destroy any tracked visuals before they orphan.
        for (int i = activeBeamVisuals.Count - 1; i >= 0; i--)
        {
            var v = activeBeamVisuals[i];
            if (v != null) Destroy(v);
        }
        activeBeamVisuals.Clear();
        ClearBeamAuras();
        ClearAllTelegraphs();
        DestroyShieldVisual();

        if (GameManager.Instance != null)
            GameManager.Instance.OnFinalBossKilled();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null && !dying)
        {
            // If the boss is destroyed mid-fight without OnBossKilled (scene
            // unload / cleanup), still clear the spawned-flag so a fresh run
            // doesn't think the boss is still alive somewhere.
            GameManager.Instance.NotifyFinalBossDespawned();
        }
    }

    // -------------------- Master state machine --------------------

    private IEnumerator BossLifecycle()
    {
        // Spawn phase: telegraph then 1HP slam.
        yield return SpawnAttack();
        SpawnAttackResolved = true;

        // Opening attack: the giant sweeping death beam, BEFORE the normal
        // melee/ranged rotation begins. This is the first thing the player
        // has to read & dodge after the unavoidable spawn slam.
        yield return SweepingBeamPattern();
        yield return new WaitForSeconds(interPatternPause);

        // Hand off to the regular pattern loop. Lives in its own method so
        // the rage phase can stop & restart the master coroutine here without
        // re-running the spawn slam or the opening sweeping beam.
        yield return RegularLoop();
    }

    /// <summary>
    /// Post-opening pattern rotation. Alternates Melee ↔ Ranged with a brief
    /// pause between sub-patterns. Extracted so the rage phase can restart
    /// the master coroutine partway through the fight without redoing the
    /// spawn attack or the opening sweeping beam.
    /// </summary>
    private IEnumerator RegularLoop()
    {
        bool meleeFirst = Random.value < 0.5f;
        while (IsActive && enemy != null && !enemy.IsDead)
        {
            if (meleeFirst) yield return MeleePattern();
            else            yield return RangedPattern();
            meleeFirst = !meleeFirst;
            yield return new WaitForSeconds(interPatternPause);
        }
    }

    // -------------------- Rage Phase --------------------

    /// <summary>
    /// Watches lifeTimer and triggers a rage phase at <see cref="ragePhaseFirstAt"/>,
    /// then every <see cref="ragePhaseInterval"/> seconds. Each trigger:
    ///   * Stops the master + beam coroutines so the boss is fully paused.
    ///   * Plays an orange "gathering" particle channel for ragePhaseChannelDuration.
    ///   * Doubles incoming damage taken AND outgoing damage dealt (stacks
    ///     exponentially across triggers).
    ///   * Adds <see cref="ragePhaseExtraProjectilesPerTrigger"/> projectiles to
    ///     every multi-projectile attack (additive per trigger).
    ///   * Restarts the master coroutine at <see cref="RegularLoop"/> so the
    ///     boss doesn't redo its spawn attack / opening sweeping beam, and
    ///     restarts the death-beam routine.
    /// Runs as a parallel coroutine so it can pre-empt whichever sub-pattern
    /// is active when the timer crosses the threshold.
    /// </summary>
    private IEnumerator RagePhaseWatcher()
    {
        while (IsActive)
        {
            // Don't trigger before the spawn slam resolves — the player has to
            // see the intro first, and stopping the master mid-spawn is bad.
            if (!SpawnAttackResolved) { yield return null; continue; }
            if (rageActive)            { yield return null; continue; }

            if (lifeTimer >= nextRagePhaseTime)
            {
                yield return TriggerRagePhase();
                // Schedule the next phase. Anchor to the previous trigger
                // time + interval so we don't drift forward if the channel
                // takes longer than expected.
                nextRagePhaseTime = nextRagePhaseTime + Mathf.Max(1f, ragePhaseInterval);
            }
            yield return null;
        }
    }

    /// <summary>
    /// Pause the boss, run the orange-gathering channel, then apply the
    /// exponential damage-taken / damage-dealt boosts and add extra projectiles.
    /// </summary>
    private IEnumerator TriggerRagePhase()
    {
        rageActive = true;

        // Stop the master state machine + the constant death beam so the
        // boss truly pauses. We restart them after the channel resolves.
        if (masterRoutine != null) { StopCoroutine(masterRoutine); masterRoutine = null; }
        if (beamRoutine   != null) { StopCoroutine(beamRoutine);   beamRoutine   = null; }
        if (bonusVolleyRoutine != null) { StopCoroutine(bonusVolleyRoutine); bonusVolleyRoutine = null; }
        isAerialOrSweepingActive = false;
        if (beamLine != null) beamLine.enabled = false;
        ClearAllTelegraphs();
        // Destroy any in-flight pattern visuals parented to the boss
        // (mainly the SweepingBeam line) — without this, an interrupted
        // sweep would leave a stale beam glued to the boss for the rest of
        // the fight.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null) continue;
            string n = child.name;
            if (n == "SweepingBeam" || n == "BossTelegraph") Destroy(child.gameObject);
        }
        // Destroy any beam visuals spawned by DeathBeamRoutine, SweepingBeamPattern,
        // or DashEndBeam. These are NOT parented to the boss (Instantiate(prefab)
        // with no parent), so the child-loop above doesn't catch them. Each spawn
        // site registers into activeBeamVisuals; the rage StopCoroutine drops the
        // local references so without this they'd just hang forever in the scene.
        for (int i = activeBeamVisuals.Count - 1; i >= 0; i--)
        {
            var v = activeBeamVisuals[i];
            if (v != null) Destroy(v);
        }
        activeBeamVisuals.Clear();
        // Same risk for the lightning auras chained along whichever beam was
        // mid-fire — they're top-level GameObjects too.
        ClearBeamAuras();

        // Freeze in place. Velocity is zeroed every frame in the channel
        // loop too, in case the rigidbody gets nudged by colliders.
        if (rb != null) rb.velocity = Vector3.zero;

        // Telegraph ring + pulsing aura light.
        GameObject ring = MakeGroundRing(transform.position, ragePhaseTelegraphRadius, ragePhaseColor);
        ring.transform.SetParent(transform, true);

        GameObject auraGo = new GameObject("RageAura");
        auraGo.transform.SetParent(transform, false);
        auraGo.transform.localPosition = Vector3.up * 1.5f;
        var auraLight = auraGo.AddComponent<Light>();
        auraLight.type = LightType.Point;
        auraLight.color = ragePhaseColor;
        auraLight.intensity = 4f;
        auraLight.range = 14f;
        auraLight.shadows = LightShadows.None;

        float duration = Mathf.Max(0.05f, ragePhaseChannelDuration);
        float t = 0f;
        float gatherTimer = 0f;
        float shakeTimer = 0f;
        // Initial bump on channel start so the player feels the rage kicking in.
        ShakeCamera(ragePhaseChannelShakeAmplitude * 0.6f, Mathf.Max(0.05f, ragePhaseChannelShakeInterval * 1.5f));
        while (t < duration && this != null)
        {
            float dt = Time.deltaTime;
            t += dt;
            gatherTimer += dt;
            shakeTimer += dt;
            float k = t / duration;

            if (rb != null) rb.velocity = Vector3.zero;
            auraLight.intensity = Mathf.Lerp(4f, 18f, k * k);
            // Ring pulses inward then back so it doesn't read as a static circle.
            float ringScale = Mathf.Lerp(1.0f, 0.6f + 0.15f * Mathf.Sin(t * 12f), k);
            ring.transform.localScale = Vector3.one * ringScale;

            if (gatherTimer >= Mathf.Max(0.01f, ragePhaseGatherSpawnInterval))
            {
                gatherTimer = 0f;
                EmitGatherBurst();
            }

            // Rolling channel shake — builds up over the channel so the
            // tension grows toward the release. Each tick fires a short
            // overlapping shake; CameraFollow.Shake already merges them
            // intelligently (peak amplitude wins).
            if (shakeTimer >= Mathf.Max(0.02f, ragePhaseChannelShakeInterval))
            {
                shakeTimer = 0f;
                float amp = ragePhaseChannelShakeAmplitude * Mathf.Lerp(0.35f, 1f, k);
                ShakeCamera(amp, Mathf.Max(0.05f, ragePhaseChannelShakeInterval * 1.5f));
            }
            yield return null;
        }

        // Apply the boost. Stacks exponentially because we MULTIPLY the
        // running multipliers by the per-trigger factor — each phase doubles
        // (×2, then ×4, then ×8, …) by default.
        ragePhaseTriggerCount++;
        attackDamageMultiplier  *= Mathf.Max(0.01f, ragePhaseDamageDealtMultiplier);
        incomingDamageMultiplier *= Mathf.Max(0.01f, ragePhaseDamageTakenMultiplier);
        extraProjectileCount    += Mathf.Max(0, ragePhaseExtraProjectilesPerTrigger);

        // Resolve burst: orange shockwave + heavy particle burst + camera shake.
        BossShockwave.Spawn(transform.position,
                            Mathf.Max(1f, ragePhaseBurstRadius),
                            ragePhaseColor,
                            Mathf.Max(0.05f, ragePhaseBurstDuration));
        HitParticles.EmitBurst(transform.position + Vector3.up * 1.0f,
            Vector3.up,
            count: 32, speed: 11f, lifetime: 0.7f, size: 0.22f,
            color: ragePhaseColor, spreadAngle: 80f, useGravity: false);
        ShakeCamera(ragePhaseShakeAmplitude, ragePhaseShakeDuration);

        if (ring    != null) Destroy(ring);
        if (auraGo  != null) Destroy(auraGo);

        // If the rage interrupted an aerial pattern, the boss could be in
        // the air (fly-up phase, between waves, or mid-fall). Drop it back
        // to homeY before resuming so the next pattern doesn't dash / bounce
        // / spam from a floating altitude.
        if (transform.position.y > homeY + 0.5f && IsActive && enemy != null && !enemy.IsDead)
            yield return FallToHomeY();

        // Re-enable the running fight. RegularLoop is safe to start mid-fight
        // (no spawn slam, no opening beam); the death-beam routine waits one
        // frame internally so it picks up cleanly.
        if (IsActive && enemy != null && !enemy.IsDead)
        {
            masterRoutine = StartCoroutine(RegularLoop());
            beamRoutine   = StartCoroutine(DeathBeamRoutine());
            if (bonusVolleyRoutine == null)
                bonusVolleyRoutine = StartCoroutine(BonusAerialVolleysRoutine());
        }

        rageActive = false;
    }

    /// <summary>
    /// Animate the boss falling from its current position back down to homeY
    /// (the ground level captured at Awake). Eases with a quadratic so the
    /// landing feels weighty. XZ is held; only y changes. Caps the time so a
    /// huge altitude doesn't stretch into a slow drop.
    /// </summary>
    private IEnumerator FallToHomeY()
    {
        if (rb != null) rb.velocity = Vector3.zero;
        Vector3 from = transform.position;
        Vector3 to = new Vector3(from.x, homeY, from.z);
        // Time scales with altitude (taller drop = a bit longer fall),
        // capped so it never drags. ~0.4s for typical aerialFlyUpHeight.
        float dist = Mathf.Max(0f, from.y - homeY);
        float fall = Mathf.Clamp(dist / 32f + 0.18f, 0.18f, 0.55f);
        float t = 0f;
        while (t < fall && this != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fall);
            // Ease-in (k*k) so the boss accelerates downward — reads as gravity.
            Vector3 p = Vector3.Lerp(from, to, k * k);
            Vector3 clamped = ClampToArena(p); clamped.y = p.y;
            transform.position = clamped;
            if (rb != null) rb.velocity = Vector3.zero;
            yield return null;
        }
        transform.position = ClampToArena(to);
        if (rb != null) rb.velocity = Vector3.zero;

        // Tiny landing punctuation: light shake + a smattering of orange
        // particles so the drop reads as a deliberate beat instead of a teleport.
        ShakeCamera(0.18f, 0.16f);
        HitParticles.EmitBurst(to + Vector3.up * 0.1f, Vector3.up,
            count: 14, speed: 6f, lifetime: 0.45f, size: 0.18f,
            color: ragePhaseColor, spreadAngle: 80f, useGravity: true);
    }

    /// <summary>
    /// Spawn one tick of the gathering effect: small particles spawn at random
    /// positions on a ring around the boss and fly inward toward the boss
    /// center. Uses HitParticles with a tight spreadAngle + useGravity=false
    /// so the particles read as streaks pulled toward the boss.
    /// </summary>
    private void EmitGatherBurst()
    {
        int count = Mathf.Max(1, ragePhaseGatherParticlesPerBurst);
        Vector3 center = transform.position + Vector3.up * 1.0f;
        float radius = Mathf.Max(0.5f, ragePhaseGatherRadius);
        for (int i = 0; i < count; i++)
        {
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
            offset.y = Random.Range(-0.3f, 1.6f);
            Vector3 spawnPos = center + offset;
            Vector3 inward = (center - spawnPos);
            if (inward.sqrMagnitude < 0.0001f) inward = Vector3.up;
            HitParticles.EmitBurst(spawnPos, inward.normalized,
                count: 1,
                speed: ragePhaseGatherParticleSpeed,
                lifetime: ragePhaseGatherParticleLifetime,
                size: ragePhaseGatherParticleSize,
                color: ragePhaseColor,
                spreadAngle: 5f,
                useGravity: false);
        }
    }

    // -------------------- Spawn attack --------------------

    private IEnumerator SpawnAttack()
    {
        // Pulsing red light on the boss + ground ring telegraph.
        GameObject auraGo = MakeGroundRing(transform.position, spawnTelegraphRadius, spawnTelegraphColor);
        auraGo.transform.SetParent(transform, true);

        GameObject lightGo = new GameObject("SpawnAura");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = Vector3.up * 1.5f;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = spawnTelegraphColor;
        l.intensity = 6f;
        l.range = 12f;
        l.shadows = LightShadows.None;

        float t = 0f;
        while (t < spawnTelegraphTime)
        {
            t += Time.deltaTime;
            float k = t / spawnTelegraphTime;
            l.intensity = Mathf.Lerp(3f, 18f, k * k);
            float ringScale = Mathf.Lerp(1.0f, 0.4f, k);
            auraGo.transform.localScale = Vector3.one * ringScale;
            yield return null;
        }

        // Slam: take the player down to half their MAX HP regardless of
        // i-frames. SetCurrentHP only ever lowers HP, so a player already
        // below 50% won't be healed up to 50% — they just stay where they are.
        if (player == null) player = Hero.Instance;
        if (player != null && !player.IsDead)
            player.SetCurrentHP(player.MaxHP * 0.5f);

        // Big shockwave + heavy shake on resolve.
        BossShockwave.Spawn(transform.position, 26f, new Color(1f, 0.2f, 0.2f), 0.55f);
        ShakeCamera(spawnShakeAmplitude, spawnShakeDuration);

        if (auraGo != null) Destroy(auraGo);
        if (lightGo != null) Destroy(lightGo);

        // After the slam, follow up with telegraphed grid arrow volleys.
        // This is part of the spawn-attack sequence — only runs once on
        // initial boss spawn (and again on subsequent Continue spawns
        // because BossLifecycle calls SpawnAttack each time).
        if (spawnGridVolleysEnabled && aerialArrowPrefab != null && spawnGridVolleyCount > 0)
            yield return SpawnAttackGridVolleys();
    }

    /// <summary>
    /// Continuous side coroutine that fires single uniform aerial-style
    /// volleys throughout the fight on a fixed interval. Suppressed while
    /// AerialPattern or SweepingBeamPattern is running (those patterns
    /// already include their own bonus volleys, so we'd double up).
    /// </summary>
    private IEnumerator BonusAerialVolleysRoutine()
    {
        // Wait one cycle before the FIRST bonus volley fires so the spawn
        // attack and its post-slam volleys finish first.
        float waitTimer = 0f;
        while (IsActive)
        {
            float dt = Time.deltaTime;
            waitTimer += dt;

            if (!bonusVolleysContinuousEnabled
                || aerialArrowPrefab == null
                || !SpawnAttackResolved
                || isAerialOrSweepingActive)
            {
                // Don't accrue interval time while suppressed — we want a
                // clean N-second gap from when the suppression ENDS, not
                // potentially firing immediately because the timer expired
                // during the AerialPattern.
                if (isAerialOrSweepingActive) waitTimer = 0f;
                yield return null;
                continue;
            }

            if (waitTimer >= Mathf.Max(0.5f, bonusVolleyInterval))
            {
                waitTimer = 0f;
                yield return SpawnAttackOneGridVolley(0);
            }
            else
            {
                yield return null;
            }
        }
    }

    /// <summary>
    /// Fires <see cref="spawnGridVolleyCount"/> sequential aerial-style
    /// arrow volleys. Each volley uses the same predict-ahead + edge-ring
    /// spawn pattern as the main AerialPattern wave, so the player gets
    /// three back-to-back bonus volleys after the spawn slam (and again
    /// alongside every Sweeping Beam attack).
    /// </summary>
    private IEnumerator SpawnAttackGridVolleys()
    {
        int volleys = Mathf.Max(0, spawnGridVolleyCount);
        for (int v = 0; v < volleys; v++)
        {
            yield return SpawnAttackOneGridVolley(v);
            if (v < volleys - 1)
                yield return new WaitForSeconds(Mathf.Max(0f, spawnGridIntervalBetween));
            if (!IsActive) yield break;
        }
    }

    private IEnumerator SpawnAttackOneGridVolley(int volleyIndex)
    {
        if (aerialArrowPrefab == null) yield break;

        // Each volley picks a slightly different lead so volley 0 aims at
        // ~now, volley 1 a bit ahead, volley 2 further ahead — gives the
        // three volleys distinct feel without any rigid "grid" math.
        float lead = (volleyIndex + 0.5f) * Mathf.Max(0f, spawnGridLeadStep);
        Vector3 predicted = PredictPlayerPosition(lead);

        // Use the snapshotted ground y, NOT transform.position.y. The boss
        // can be mid-bounce / mid-aerial when a periodic bonus volley
        // fires; without this anchor the arrows would spawn high in the
        // sky and arc down past the player.
        float arrowY = bossGroundY + aerialArrowSpawnHeight;
        Vector3 predictedAtArrowY = new Vector3(predicted.x, arrowY, predicted.z);

        int count = ScaledCount(Mathf.Max(1, spawnGridArrowsPerVolley));
        List<Vector3> spawnPoints = new List<Vector3>(count);
        List<Vector3> targets     = new List<Vector3>(count);
        List<LineRenderer> tels   = new List<LineRenderer>(count);

        for (int a = 0; a < count; a++)
        {
            float angle = (360f / count) * a;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * spawnGridEdgeSpawnRadius;
            Vector3 from = predictedAtArrowY + offset;
            from.y = arrowY;

            spawnPoints.Add(from);
            targets.Add(predictedAtArrowY);

            Vector3 telDir = predictedAtArrowY - from;
            Vector3 telEnd = predictedAtArrowY;
            if (telDir.sqrMagnitude > 0.0001f && aerialTelegraphExtensionDistance > 0f)
                telEnd = predictedAtArrowY + telDir.normalized * aerialTelegraphExtensionDistance;
            LineRenderer lr = SpawnAerialTelegraph(from, telEnd, aerialTelegraphColor);
            tels.Add(lr);
        }

        yield return new WaitForSeconds(Mathf.Max(0.05f, spawnGridTelegraphTime));

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Vector3 dir = (targets[i] - spawnPoints[i]);
            if (dir.sqrMagnitude < 0.0001f) continue;
            dir.Normalize();
            EnemyProjectile p = SpawnProjectile(aerialArrowPrefab, spawnPoints[i], Quaternion.LookRotation(dir, Vector3.up));
            if (spawnGridArrowSpeed > 0f) p.speed = spawnGridArrowSpeed;
            ApplyArrowScale(p, spawnGridArrowScale);
            p.Launch(dir, ScaledDamage(spawnGridArrowDamage));
        }

        for (int k = 0; k < tels.Count; k++) if (tels[k] != null) Destroy(tels[k].gameObject);
    }

    // -------------------- Melee mode --------------------

    private IEnumerator MeleePattern()
    {
        // Spin up the parallel homing barrage so it runs alongside whichever
        // sub-pattern (dashes or bounces) we pick this round. Side-coroutine
        // is stopped as soon as the sub-pattern finishes so it doesn't bleed
        // into the inter-pattern pause or the next ranged phase.
        Coroutine barrage = null;
        if (meleeHomingProjectilePrefab != null)
            barrage = StartCoroutine(MeleeHomingBarrage());

        // Enrage phase: after enragePhaseTime seconds of boss life, melee
        // and ranged stack on top of each other. We start a side-fire
        // coroutine that keeps spitting spam waves while the dash / bounce
        // pattern runs underneath it.
        Coroutine enragedFire = null;
        if (lifeTimer >= enragePhaseTime && spamArrowPrefab != null)
            enragedFire = StartCoroutine(EnragedRangedSideFire());

        if (meleeNextIsBounce) yield return BouncePattern();
        else                   yield return DashPattern();
        meleeNextIsBounce = !meleeNextIsBounce;

        if (barrage     != null) StopCoroutine(barrage);
        if (enragedFire != null) StopCoroutine(enragedFire);
    }

    /// <summary>
    /// Side coroutine started during enraged melee phases. Continuously
    /// fires the rotating 8-direction spam wave from the boss's current
    /// position so ranged pressure stacks on top of the melee pattern.
    /// Doesn't move the boss — the melee sub-pattern owns transform writes.
    /// </summary>
    private IEnumerator EnragedRangedSideFire()
    {
        // FireSpamWave reads spamGroundY for the y of the spawn ring. Keep
        // it tracking the boss so even airborne bounce moments still spawn
        // arrows at the boss's current ground level.
        float fanAngle = 0f;
        float waveTimer = 0f;
        while (IsActive)
        {
            spamGroundY = transform.position.y;
            float dt = Time.deltaTime;
            waveTimer -= dt;
            fanAngle  += spamRotationSpeed * dt;
            if (waveTimer <= 0f)
            {
                FireSpamWave(fanAngle);
                waveTimer = Mathf.Max(0.05f, ScaledInterval(enragedRangedSpamInterval));
            }
            yield return null;
        }
    }

    /// <summary>
    /// Parallel side-routine that fires periodic ring-fans of homing
    /// EnemyProjectiles while the boss is mid-dashing or mid-bouncing. Stops
    /// as soon as MeleePattern stops it (or when IsActive flips false). Also
    /// self-terminates when a rage phase begins so the boss is fully paused
    /// during the channel; a fresh MeleePattern after the channel will spin
    /// up a new barrage.
    /// </summary>
    private IEnumerator MeleeHomingBarrage()
    {
        if (meleeHomingBarrageStartDelay > 0f)
            yield return new WaitForSeconds(meleeHomingBarrageStartDelay);
        while (IsActive && !rageActive)
        {
            FireHomingBarrage();
            yield return new WaitForSeconds(Mathf.Max(0.05f, ScaledInterval(meleeHomingBarrageInterval)));
        }
    }

    private void FireHomingBarrage()
    {
        if (meleeHomingProjectilePrefab == null) return;
        Vector3 origin = transform.position + Vector3.up * meleeHomingSpawnHeight;
        int count = ScaledCount(meleeHomingArrowsPerBarrage);
        // Random ring offset so consecutive barrages don't overlap directions.
        float offsetDeg = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i + offsetDeg + Random.Range(-meleeHomingAngleJitter, meleeHomingAngleJitter);
            float rad = angle * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            EnemyProjectile p = SpawnProjectile(meleeHomingProjectilePrefab, origin, Quaternion.LookRotation(dir, Vector3.up));
            // Force homing on regardless of what the prefab was authored with.
            p.homing = true;
            if (meleeHomingArrowSpeed > 0f) p.speed = meleeHomingArrowSpeed;
            p.turnSpeed      = meleeHomingTurnSpeed;
            p.homingDuration = meleeHomingDuration;
            p.homingMaxRange = meleeHomingMaxRange;
            ApplyArrowScale(p, meleeHomingArrowScale);
            p.Launch(dir, ScaledDamage(meleeHomingArrowDamage));
        }
    }

    private IEnumerator DashPattern()
    {
        if (player == null) player = Hero.Instance;
        rb.velocity = Vector3.zero;

        // Lock-on stepper: per dash, (1) snapshot the player's current
        // position, (2) hold for dashLockOnDelay seconds while the boss winds
        // up — the snapshot does NOT update during this delay, so a player
        // who steps out of the line dodges the dash, (3) dash to the snapshot
        // (plus overshoot).
        for (int i = 0; i < dashCount; i++)
        {
            Vector3 from = transform.position;

            // (1) Snapshot.
            Vector3 snapshot = (player != null && !player.IsDead)
                ? player.transform.position
                : from + transform.forward;
            Vector3 toSnapshot = snapshot - from;
            toSnapshot.y = 0f;
            if (toSnapshot.sqrMagnitude < 0.0001f) toSnapshot = transform.forward;
            Vector3 dirN = toSnapshot.normalized;
            Vector3 target = snapshot + dirN * dashOvershoot;
            target.y = from.y;
            // Keep the dash inside the arena.
            target = ClampToArena(target);

            // (2) Lock-on delay. Boss stands still and faces the snapshot so
            // the player can read where the dash is about to go. Shortened
            // by attackSpeedMultiplier so faster final-boss spawns react quicker.
            float lockDelay = ScaledInterval(dashLockOnDelay);
            if (lockDelay > 0f)
            {
                rb.velocity = Vector3.zero;
                if (dirN.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(dirN, Vector3.up);
                yield return new WaitForSeconds(lockDelay);
            }

            // (3) Dash to the (frozen) snapshot.
            yield return DoDash(from, target);

            // Quick non-telegraphed sweep beam in front of the boss along the
            // dash direction. Runs in parallel so the dash chain stays tight.
            if (dashEndBeamEnabled)
            {
                Vector3 forward = (target - from);
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
                forward.Normalize();
                StartCoroutine(DashEndSweepBeam(forward));
            }

            // Each successful dash adds a shield layer (per spec).
            shieldStacks++;
            EnsureShieldVisual();

            yield return new WaitForSeconds(ScaledInterval(dashGap));
        }
    }

    private IEnumerator DoDash(Vector3 from, Vector3 target)
    {
        rb.velocity = Vector3.zero;

        // Pre-compute the dash direction & its perpendiculars so we can fire
        // side arrows perpendicular to the path. Skip if the dash is degenerate.
        Vector3 dashFlatDir = (target - from);
        dashFlatDir.y = 0f;
        bool hasDir = dashFlatDir.sqrMagnitude > 0.0001f;
        if (hasDir) dashFlatDir.Normalize();
        Vector3 perpRight = hasDir ? Vector3.Cross(Vector3.up, dashFlatDir).normalized : Vector3.right;
        Vector3 perpLeft  = -perpRight;

        int sideShots = dashSideArrowsEnabled ? Mathf.Max(0, dashSideArrowCount) : 0;
        int nextSideShot = 0;

        // Drop one burn patch right at the dash's starting position so the
        // trail has something visible from frame zero.
        if (dashBurnEnabled) DropDashBurnPatch(from);
        float burnTimer = 0f;

        // Lerp from current position → target over dashDuration. The boss does
        // NOT teleport back to a "home" position between dashes — it stays
        // wherever the previous dash left it, then aims again at the player.
        float t = 0f;
        bool hitPlayer = false;
        while (t < dashDuration)
        {
            float dt = Time.deltaTime;
            t += dt;
            burnTimer += dt;
            float k = Mathf.Clamp01(t / dashDuration);
            Vector3 p = Vector3.Lerp(from, target, k);
            p.y = from.y;
            p = ClampToArena(p);
            transform.position = p;

            // Fire side arrow pairs at evenly-spaced k thresholds. Spacing is
            // 1 / (sideShots + 1) so the shots land at 25/50/75 % for count=3.
            while (sideShots > 0 && nextSideShot < sideShots
                   && k >= (nextSideShot + 1f) / (sideShots + 1f))
            {
                FireDashSideArrow(p, from.y, perpLeft);
                FireDashSideArrow(p, from.y, perpRight);
                nextSideShot++;
            }

            // Drip burn patches along the dash path so the trail is continuous.
            if (dashBurnEnabled && burnTimer >= dashBurnSpawnInterval)
            {
                burnTimer = 0f;
                Vector3 patchPos = p; patchPos.y = from.y;
                DropDashBurnPatch(patchPos);
            }

            if (!hitPlayer && player != null && !player.IsDead)
            {
                Vector3 d = player.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= dashHitRadius * dashHitRadius)
                {
                    player.TakeDamage(ScaledDamage(dashDamage));
                    hitPlayer = true;
                    // Heavy shake on a hit; lighter footstep shake handled below.
                    ShakeCamera(dashShakeAmplitude, dashShakeDuration);
                }
            }
            yield return null;
        }
        transform.position = ClampToArena(target);
        // Always shake a little on dash impact so the speed reads, even on miss.
        if (!hitPlayer) ShakeCamera(dashFootstepShakeAmplitude, dashFootstepShakeDuration);
    }

    /// <summary>
    /// Spawn one aerial-style arrow flying along <paramref name="dir"/> from
    /// the boss's current position. Re-uses the aerial arrow prefab + tunables
    /// so the dash side shots feel like part of the same projectile family.
    /// </summary>
    private void FireDashSideArrow(Vector3 bossPos, float groundY, Vector3 dir)
    {
        if (aerialArrowPrefab == null) return;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir = dir.normalized;

        Vector3 spawn = bossPos;
        spawn.y = groundY + aerialArrowSpawnHeight;

        EnemyProjectile p = SpawnProjectile(aerialArrowPrefab, spawn, Quaternion.LookRotation(dir, Vector3.up));
        if (aerialArrowSpeed > 0f) p.speed = aerialArrowSpeed;
        ApplyArrowScale(p, aerialArrowScale);
        p.Launch(dir, ScaledDamage(aerialArrowDamage));
    }

    /// <summary>
    /// Spawn a single dash burn patch at the given world position, scaled
    /// by the per-spawn damage multiplier.
    /// </summary>
    private void DropDashBurnPatch(Vector3 pos)
    {
        if (!dashBurnEnabled || dashBurnRadius <= 0f || dashBurnDuration <= 0f) return;
        BossBurnPatch.Spawn(pos, dashBurnRadius, dashBurnDuration,
                            ScaledDamage(dashBurnDamagePerSecond), dashBurnColor);
    }

    /// <summary>
    /// Quick non-telegraphed death beam fired in front of the boss right
    /// after a dash lands. Sweeps a small arc centered on the dash direction
    /// and damages the player on the line during the brief lifetime. Runs as
    /// a parallel coroutine so consecutive dashes don't slow down.
    /// </summary>
    private IEnumerator DashEndSweepBeam(Vector3 forwardDir)
    {
        if (dashEndBeamDuration <= 0.0001f) yield break;
        if (forwardDir.sqrMagnitude < 0.0001f) yield break;
        forwardDir = forwardDir.normalized;

        // Pick a sweep direction (random L→R or R→L so consecutive dashes
        // don't carve the same arc).
        float halfSweep = Mathf.Max(0f, dashEndBeamSweepDegrees) * 0.5f;
        bool reverse = Random.value < 0.5f;
        Vector3 startDir = Quaternion.AngleAxis(reverse ?  halfSweep : -halfSweep, Vector3.up) * forwardDir;
        Vector3 endDir   = Quaternion.AngleAxis(reverse ? -halfSweep :  halfSweep, Vector3.up) * forwardDir;

        // Visual: prefer the bow Death Beam prefab (so it gets bloom),
        // otherwise spawn a transient LineRenderer.
        GameObject beamVisual = null;
        LineRenderer beamLR = null;
        if (deathBeamVisualPrefab != null)
        {
            beamVisual = Instantiate(deathBeamVisualPrefab);
            // Register so rage phase can destroy this visual if it interrupts
            // the dash mid-flight.
            activeBeamVisuals.Add(beamVisual);
            // Bow's DeathBeam script self-destructs without an owner; strip it.
            foreach (var db in beamVisual.GetComponentsInChildren<DeathBeam>()) Destroy(db);
            // Helper picks the right scaling convention (cube-along-Z vs FF self-scale).
            SetupBeamVisualScale(beamVisual, beamFireVisualRadius * 2f, beamRange);
        }
        else
        {
            GameObject go = new GameObject("DashEndBeam", typeof(LineRenderer));
            // Track the LineRenderer GameObject too — same leak risk if rage
            // cancels DashPattern mid-coroutine.
            activeBeamVisuals.Add(go);
            beamLR = go.GetComponent<LineRenderer>();
            beamLR.useWorldSpace = true;
            beamLR.positionCount = 2;
            beamLR.startWidth = dashEndBeamWidth;
            beamLR.endWidth   = dashEndBeamWidth;
            beamLR.material = GetTelegraphMaterial();
            beamLR.startColor = dashEndBeamColor;
            beamLR.endColor   = dashEndBeamColor;
            beamLR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamLR.receiveShadows = false;
        }

        float t = 0f;
        float damageTickT = 0f;
        const float damageTick = 0.05f;
        while (t < dashEndBeamDuration && this != null)
        {
            t += Time.deltaTime;
            damageTickT += Time.deltaTime;
            float k = Mathf.Clamp01(t / dashEndBeamDuration);
            Vector3 dir = Vector3.Slerp(startDir, endDir, k).normalized;
            Vector3 origin = transform.position + Vector3.up * beamOriginHeight;
            Vector3 endpoint = origin + dir * beamRange;

            if (beamVisual != null)
            {
                OrientBeamVisual(beamVisual, origin, dir, beamRange);
            }
            else if (beamLR != null)
            {
                beamLR.SetPosition(0, origin);
                beamLR.SetPosition(1, endpoint);
                // Fade the LR a bit so the brief beam reads as a flash.
                Color c = dashEndBeamColor; c.a = 1f - k * 0.4f;
                beamLR.startColor = c;
                beamLR.endColor   = c;
            }

            if (damageTickT >= damageTick && player != null && !player.IsDead)
            {
                Vector3 toPlayer = (player.transform.position + Vector3.up * beamTargetHeight) - origin;
                float along = Mathf.Clamp(Vector3.Dot(toPlayer, dir), 0f, beamRange);
                Vector3 closest = origin + dir * along;
                float perpDist = Vector3.Distance(closest, player.transform.position + Vector3.up * beamTargetHeight);
                if (perpDist <= dashEndBeamHitRadius)
                    player.TakeDamage(ScaledDamage(dashEndBeamDamagePerSecond) * damageTickT);
                damageTickT = 0f;
            }

            yield return null;
        }

        if (beamVisual != null) { activeBeamVisuals.Remove(beamVisual); Destroy(beamVisual); }
        if (beamLR != null)     { activeBeamVisuals.Remove(beamLR.gameObject); Destroy(beamLR.gameObject); }
    }

    private IEnumerator BouncePattern()
    {
        if (player == null) player = Hero.Instance;
        rb.velocity = Vector3.zero;
        Vector3 startPos = transform.position;
        float groundY = startPos.y;

        for (int i = 0; i < bounceCount; i++)
        {
            // Predict landing.
            Vector3 land = PredictPlayerPosition(bounceTelegraphTime + bounceArcTime);
            // Push past player along their motion vector.
            if (estPlayerVel.sqrMagnitude > 0.01f)
                land += estPlayerVel.normalized * bounceLeadDistance;
            land.y = groundY;
            // Keep the bounce landing inside the arena.
            land = ClampToArena(land);

            // Telegraph circle on the ground.
            GameObject ring = MakeGroundRing(land, bounceImpactRadius, bounceTelegraphColor);
            bounceTelegraphPool.Add(ring);

            // Hold telegraph briefly.
            yield return new WaitForSeconds(bounceTelegraphTime);

            // Arc through the air. Boss ascends from current pos, peaks, lands at ring center.
            Vector3 from = transform.position;
            float t = 0f;
            while (t < bounceArcTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / bounceArcTime);
                Vector3 p = Vector3.Lerp(from, land, k);
                // Parabola: peak at k=0.5 with height bounceArcHeight.
                p.y = groundY + 4f * bounceArcHeight * k * (1f - k);
                // XZ clamp every frame too — covers the case where 'from' was
                // already outside (e.g. boss got pushed) and the lerp would
                // cross the wall.
                Vector3 clamped = ClampToArena(p);
                clamped.y = p.y;
                transform.position = clamped;
                yield return null;
            }
            transform.position = ClampToArena(land);
            rb.velocity = Vector3.zero;

            // Apply AOE damage if the player is inside the impact radius.
            if (player != null && !player.IsDead)
            {
                Vector3 d = player.transform.position - land; d.y = 0f;
                if (d.sqrMagnitude <= bounceImpactRadius * bounceImpactRadius)
                    player.TakeDamage(ScaledDamage(bounceImpactDamage));
            }

            // Smash particles + heavy slam shake.
            HitParticles.EmitBurst(land + Vector3.up * 0.1f, Vector3.up,
                count: 24, speed: 7f, lifetime: 0.5f, size: 0.18f,
                color: bounceTelegraphColor, spreadAngle: 80f, useGravity: true);
            ShakeCamera(bounceShakeAmplitude, bounceShakeDuration);

            // Expanding shockwave that deals delayed damage as it sweeps out.
            // The immediate slam-impact damage above hits anyone inside
            // bounceImpactRadius the moment the boss lands; this wave then
            // fans out and clips the player if they don't keep moving away
            // (or run inside the inner radius, but at that point they took
            // the slam damage anyway).
            if (bounceShockwaveEnabled && bounceShockwaveDamage > 0f && bounceShockwaveMaxRadius > 0f)
            {
                BossShockwave.SpawnDamaging(
                    center: land,
                    maxRadius: bounceShockwaveMaxRadius,
                    color: bounceShockwaveColor,
                    duration: bounceShockwaveDuration,
                    damage: ScaledDamage(bounceShockwaveDamage),
                    damageBandWidth: bounceShockwaveBandWidth);
            }

            // Cleanup the telegraph ring for this bounce.
            if (ring != null) Destroy(ring);
        }

        ClearBounceTelegraphs();
    }

    // -------------------- Ranged mode --------------------

    private IEnumerator RangedPattern()
    {
        // Cycle through the three ranged sub-patterns in order.
        int idx = rangedSubpatternIndex % 3;
        rangedSubpatternIndex++;
        switch (idx)
        {
            case 0: yield return SpamPattern();          break;
            case 1: yield return AerialPattern();        break;
            case 2: yield return SweepingBeamPattern();  break;
        }
    }

    private IEnumerator SpamPattern()
    {
        if (player == null) player = Hero.Instance;
        // Snapshot the ground y at pattern start. Bosses can have inflated
        // scale (the SlimeKing prefab is 2.5×), which puts transform.position.y
        // well above the floor — using it for arrow spawn height makes arrows
        // fly above the player. Anchoring to the START y, plus the per-pattern
        // spamArrowSpawnHeight, keeps every arrow at a stable, low world y.
        spamGroundY = transform.position.y;
        float t = 0f;
        float waveTimer = 0f;
        float fanAngle = 0f;

        while (t < spamDuration && IsActive)
        {
            t += Time.deltaTime;
            waveTimer -= Time.deltaTime;
            fanAngle += spamRotationSpeed * Time.deltaTime;

            // Slow chase movement.
            if (player != null && !player.IsDead)
            {
                Vector3 toP = player.transform.position - transform.position;
                toP.y = 0f;
                if (toP.sqrMagnitude > 4f)
                {
                    Vector3 v = toP.normalized * spamChaseSpeed;
                    rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
                }
                else
                {
                    rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
                }
            }

            if (waveTimer <= 0f)
            {
                FireSpamWave(fanAngle);
                waveTimer = ScaledInterval(spamWaveInterval);
            }
            yield return null;
        }

        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    private void FireSpamWave(float angleDeg)
    {
        if (spamArrowPrefab == null) return;
        // Anchor to the snapshotted ground y, NOT transform.position.y, so
        // arrows always fly at chest level regardless of how tall/scaled the
        // boss is. spamGroundY is set at the start of SpamPattern.
        Vector3 origin = transform.position;
        origin.y = spamGroundY + spamArrowSpawnHeight;
        int count = ScaledCount(spamArrowsPerWave);
        for (int i = 0; i < count; i++)
        {
            float a = (360f / count) * i + angleDeg;
            float rad = a * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            EnemyProjectile p = SpawnProjectile(spamArrowPrefab, origin, Quaternion.LookRotation(dir, Vector3.up));
            if (spamArrowSpeed > 0f) p.speed = spamArrowSpeed;
            ApplyArrowScale(p, spamArrowScale);
            p.Launch(dir, ScaledDamage(spamArrowDamage));
        }
    }

    private IEnumerator AerialPattern()
    {
        // Suppress the continuous bonus volleys while this pattern runs —
        // it already fires its own waves so doubling them up is overkill.
        isAerialOrSweepingActive = true;
        if (player == null) player = Hero.Instance;
        // Fly up. Snapshot the (clamped) ground position so the boss never
        // takes off from outside the arena on a degenerate spawn.
        Vector3 ground = ClampToArena(transform.position);
        Vector3 air = ground + Vector3.up * aerialFlyUpHeight;
        float t = 0f;
        while (t < aerialFlyUpTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / aerialFlyUpTime);
            Vector3 p = Vector3.Lerp(ground, air, k);
            // XZ-clamp; preserve y so the rise still happens.
            Vector3 clamped = ClampToArena(p); clamped.y = p.y;
            transform.position = clamped;
            rb.velocity = Vector3.zero;
            yield return null;
        }
        transform.position = air;

        // Run waves.
        for (int i = 0; i < aerialWaveCount; i++)
        {
            // Escape-punish (parallel): if the player has fled past
            // aerialEscapeRadius from the takeoff center when this wave
            // starts, kick off an extra punish volley as a SIDE coroutine —
            // it runs ALONGSIDE this wave's telegraph + fire instead of
            // gating the wave loop. The main uniform / random alternation
            // continues unchanged underneath.
            if (aerialEscapePunishEnabled && player != null && !player.IsDead)
            {
                Vector3 toPlayerFromCenter = player.transform.position - ground;
                toPlayerFromCenter.y = 0f;
                if (toPlayerFromCenter.sqrMagnitude > aerialEscapeRadius * aerialEscapeRadius)
                    StartCoroutine(FireAerialPunishVolley(ground.y));
            }

            // Every other wave (odd indices) becomes a per-arrow scatter:
            // each arrow rolls its OWN random target inside the radius around
            // the player. Even waves use the predict-ahead lead and share a
            // single target for all arrows in the wave.
            bool isRandomWave = (i & 1) == 1;
            Vector3 sharedPredicted;
            if (isRandomWave)
            {
                // For random waves the "shared" predicted is just the player's
                // current position — used as a fallback if a per-arrow target
                // can't be computed for some reason.
                Vector3 here = (player != null && !player.IsDead)
                    ? player.transform.position
                    : transform.position;
                sharedPredicted = new Vector3(here.x, transform.position.y, here.z);
            }
            else
            {
                // Lead = (i - half) * step. With 5 waves the middle is 2 → wave 0
                // has -2*step (lags), wave 4 has +2*step (leads).
                float lead = (i - aerialWaveCount * 0.5f + 0.5f) * aerialLeadStep;
                sharedPredicted = PredictPlayerPosition(lead);
            }

            bool fromBoss = i < aerialEdgeWaveStartIndex;

            // Telegraph: draw lines from each spawn point to the (per-arrow,
            // for random waves; shared, for predict waves) target. Spawn
            // points sit at ~aerialArrowSpawnHeight off the ground so the
            // volley reads as a horizontal sweep instead of an air strike.
            List<Vector3> spawnPoints  = new List<Vector3>();
            List<Vector3> arrowTargets = new List<Vector3>();
            List<LineRenderer> tels    = new List<LineRenderer>();
            float arrowY = ground.y + aerialArrowSpawnHeight;
            Vector3 sharedTargetAtArrowY = new Vector3(sharedPredicted.x, arrowY, sharedPredicted.z);
            int waveCount = ScaledCount(aerialArrowsPerWave);
            for (int a = 0; a < waveCount; a++)
            {
                // Pick this arrow's destination first (random per-arrow on
                // random waves; shared on predict waves).
                Vector3 perArrowTarget;
                if (isRandomWave)
                {
                    Vector3 playerCenter = (player != null && !player.IsDead)
                        ? player.transform.position
                        : transform.position;
                    Vector2 ring = Random.insideUnitCircle * Mathf.Max(0f, aerialRandomTargetRadius);
                    perArrowTarget = new Vector3(playerCenter.x + ring.x, arrowY, playerCenter.z + ring.y);
                }
                else
                {
                    perArrowTarget = sharedTargetAtArrowY;
                }
                arrowTargets.Add(perArrowTarget);

                float angle = (360f / waveCount) * a;
                Vector3 from;
                if (fromBoss)
                {
                    // Spawn at the boss's XZ but on the ground plane so the
                    // arrow flies straight at the player instead of dropping
                    // from above the boss.
                    Vector3 bossPos = transform.position;
                    from = new Vector3(bossPos.x, arrowY, bossPos.z);
                }
                else
                {
                    // Edge spawn: ring around THIS arrow's target so random
                    // waves spawn arrows from a wide range of edge points.
                    float rad = angle * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * aerialEdgeSpawnRadius;
                    from = perArrowTarget + offset;
                    from.y = arrowY;
                }
                spawnPoints.Add(from);

                // Extend the telegraph line past the target along the
                // flight direction so the line reads as a flight PATH, not as
                // the line ending at a midpoint. The arrow itself still
                // launches toward perArrowTarget — only the visual goes
                // further.
                Vector3 telDir = perArrowTarget - from;
                Vector3 telEnd = perArrowTarget;
                if (telDir.sqrMagnitude > 0.0001f && aerialTelegraphExtensionDistance > 0f)
                    telEnd = perArrowTarget + telDir.normalized * aerialTelegraphExtensionDistance;
                LineRenderer lr = SpawnAerialTelegraph(from, telEnd, aerialTelegraphColor);
                tels.Add(lr);
            }

            yield return new WaitForSeconds(aerialTelegraphTime);

            for (int a = 0; a < spawnPoints.Count; a++)
            {
                Vector3 from = spawnPoints[a];
                Vector3 to   = arrowTargets[a];
                Vector3 dir = (to - from);
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();
                if (aerialArrowPrefab != null)
                {
                    EnemyProjectile p = SpawnProjectile(aerialArrowPrefab, from, Quaternion.LookRotation(dir, Vector3.up));
                    if (aerialArrowSpeed > 0f) p.speed = aerialArrowSpeed;
                    ApplyArrowScale(p, aerialArrowScale);
                    p.Launch(dir, ScaledDamage(aerialArrowDamage));
                }
            }

            for (int k = 0; k < tels.Count; k++) if (tels[k] != null) Destroy(tels[k].gameObject);

            yield return new WaitForSeconds(Mathf.Max(0.05f, ScaledInterval(aerialWaveInterval - aerialTelegraphTime)));
        }

        // Drop back down. Clamp the landing XZ so the boss can't end up
        // outside the arena if it drifted there during the aerial waves.
        Vector3 fromAir = transform.position;
        Vector3 toGround = ClampToArena(new Vector3(fromAir.x, ground.y, fromAir.z));
        t = 0f;
        float fall = 0.6f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fall);
            Vector3 p = Vector3.Lerp(fromAir, toGround, k * k);
            // Final-fall path — clamp XZ each frame just in case.
            Vector3 clamped = ClampToArena(p); clamped.y = p.y;
            transform.position = clamped;
            rb.velocity = Vector3.zero;
            yield return null;
        }
        transform.position = toGround;
        isAerialOrSweepingActive = false;
    }

    /// <summary>
    /// Returns a unit vector pointing in the player's escape direction for
    /// the punish volley. Source priority:
    ///   1. Hero.MoveInput — raw WASD direction. This is the cleanest
    ///      "where they're trying to go" signal, completely independent of
    ///      the cursor-driven facing direction. No smoothing lag either.
    ///   2. estPlayerVel.normalized — smoothed actual velocity. Used when
    ///      the player isn't currently pressing a movement key but is still
    ///      coasting along.
    ///   3. Boss → player direction.
    ///   4. World forward.
    /// </summary>
    private Vector3 ComputePlayerForwardForPunish(Vector3 playerCenter)
    {
        if (player != null)
        {
            Vector3 input = player.MoveInput;
            input.y = 0f;
            if (input.sqrMagnitude > 0.0001f) return input.normalized;
        }
        Vector3 v = estPlayerVel; v.y = 0f;
        if (v.sqrMagnitude > 0.0001f) return v.normalized;
        Vector3 fromBoss = playerCenter - transform.position; fromBoss.y = 0f;
        if (fromBoss.sqrMagnitude > 0.0001f) return fromBoss.normalized;
        return Vector3.forward;
    }

    /// <summary>
    /// Helper that wraps SpawnTelegraphLine for any aerial-style telegraph
    /// (main wave, escape-punish volley, spawn grid volley). Shifts both
    /// endpoints down by aerialTelegraphYOffset so the telegraph lines sit
    /// closer to the ground than the actual arrow flight path.
    /// </summary>
    private LineRenderer SpawnAerialTelegraph(Vector3 from, Vector3 to, Color color)
    {
        Vector3 yShift = Vector3.up * aerialTelegraphYOffset;
        return SpawnTelegraphLine(from + yShift, to + yShift, color, aerialTelegraphLineWidth);
    }

    /// <summary>
    /// Punishment volley fired when the player escapes the aerial attack
    /// area. Each arrow uses a different player-velocity lead (from
    /// aerialPunishMinLead to aerialPunishMaxLead) so the volley fans out
    /// across "where the player is now" through "where they'll be in 1+
    /// seconds", making it hard to outrun on a single straight-line escape
    /// vector.
    /// </summary>
    private IEnumerator FireAerialPunishVolley(float groundY)
    {
        if (aerialArrowPrefab == null) yield break;
        int count = ScaledCount(Mathf.Max(1, aerialPunishArrowCount));
        float arrowY = groundY + aerialArrowSpawnHeight;

        // Live telegraph + smoothed target / spawn caches per arrow.
        Vector3[] smoothedTargets = new Vector3[count];
        Vector3[] smoothedFroms   = new Vector3[count];
        LineRenderer[] tels       = new LineRenderer[count];

        // Snapshot the player's EXIT MOMENT once: their position and
        // velocity direction when the volley starts. The spawn arc is
        // computed from these snapshots and FROZEN — the spawn points
        // don't move during the telegraph window. Only the targets
        // continue tracking the player as they move.
        Vector3 exitPlayerPos = (player != null && !player.IsDead)
            ? player.transform.position
            : transform.position;
        Vector3 exitForward = ComputePlayerForwardForPunish(exitPlayerPos);
        float spreadHalf = Mathf.Max(0f, aerialPunishFrontArcDegrees) * 0.5f;
        for (int a = 0; a < count; a++)
        {
            float t01  = count == 1 ? 0.5f : (float)a / (count - 1);
            float lead = Mathf.Lerp(aerialPunishMinLead, aerialPunishMaxLead, t01);
            Vector3 predicted = PredictPlayerPosition(lead);
            Vector3 target = new Vector3(predicted.x, arrowY, predicted.z);

            // Spawn point: fixed for this volley. Sits on the front-facing
            // arc anchored at the EXIT point with the EXIT velocity
            // direction. Each arrow rotates the exit forward by an angle
            // lerped from -spreadHalf to +spreadHalf.
            float angleDeg = count == 1 ? 0f : Mathf.Lerp(-spreadHalf, spreadHalf, t01);
            Vector3 spawnDir = Quaternion.AngleAxis(angleDeg, Vector3.up) * exitForward;
            Vector3 from = new Vector3(
                exitPlayerPos.x + spawnDir.x * aerialPunishEdgeSpawnRadius,
                arrowY,
                exitPlayerPos.z + spawnDir.z * aerialPunishEdgeSpawnRadius);
            smoothedTargets[a] = target;
            smoothedFroms[a]   = from;

            // Punish telegraphs render with a higher sortingOrder so they
            // sit ON TOP of the regular aerial wave telegraphs (which use
            // the default sortingOrder of 0).
            LineRenderer lr = SpawnAerialTelegraph(from, target, aerialTelegraphColor);
            if (lr != null) lr.sortingOrder = aerialPunishTelegraphSortingOrder;
            tels[a] = lr;
        }

        Vector3 yShift = Vector3.up * aerialTelegraphYOffset;
        float t = 0f;
        float duration = Mathf.Max(0.01f, aerialPunishTelegraphTime);
        while (t < duration && IsActive)
        {
            float dt = Time.deltaTime;
            t += dt;
            // Exponential smoothing factor — framerate-independent so the
            // visible easing rate is the same at 30 / 60 / 144 fps.
            float tau = Mathf.Max(0.0001f, aerialPunishTrackSmoothing);
            float smoothK = aerialPunishTrackSmoothing > 0f
                ? 1f - Mathf.Exp(-dt / tau)
                : 1f;

            // Spawn points (smoothedFroms[a]) are FROZEN at the exit-moment
            // snapshot — only the targets continue tracking the player as
            // they keep running. This anchors the volley's source to the
            // moment they crossed the safe-radius boundary.
            for (int a = 0; a < count; a++)
            {
                float t01  = count == 1 ? 0.5f : (float)a / (count - 1);
                float lead = Mathf.Lerp(aerialPunishMinLead, aerialPunishMaxLead, t01);
                Vector3 predicted = PredictPlayerPosition(lead);
                Vector3 target = new Vector3(predicted.x, arrowY, predicted.z);

                // Smooth ONLY the target toward the latest prediction; leave
                // smoothedFroms[a] untouched so the spawn point stays locked.
                smoothedTargets[a] = Vector3.Lerp(smoothedTargets[a], target, smoothK);

                Vector3 sFrom   = smoothedFroms[a];
                Vector3 sTarget = smoothedTargets[a];
                Vector3 telDir  = sTarget - sFrom;
                Vector3 telEnd  = sTarget;
                if (telDir.sqrMagnitude > 0.0001f && aerialTelegraphExtensionDistance > 0f)
                    telEnd = sTarget + telDir.normalized * aerialTelegraphExtensionDistance;

                LineRenderer lr = tels[a];
                if (lr != null)
                {
                    lr.SetPosition(0, sFrom + yShift);
                    lr.SetPosition(1, telEnd + yShift);
                }
            }

            yield return null;
        }

        for (int a = 0; a < count; a++)
        {
            Vector3 from = smoothedFroms[a];
            Vector3 to   = smoothedTargets[a];
            Vector3 dir = (to - from);
            if (dir.sqrMagnitude < 0.0001f) continue;
            dir.Normalize();
            EnemyProjectile p = SpawnProjectile(aerialArrowPrefab, from, Quaternion.LookRotation(dir, Vector3.up));
            if (aerialPunishArrowSpeed > 0f) p.speed = aerialPunishArrowSpeed;
            ApplyArrowScale(p, aerialPunishArrowScale);
            p.Launch(dir, ScaledDamage(aerialPunishArrowDamage));
        }

        for (int k = 0; k < tels.Length; k++) if (tels[k] != null) Destroy(tels[k].gameObject);
    }

    // -------------------- Ranged sub-pattern: Sweeping Death Beam --------------------

    /// <summary>
    /// Boss stands still, telegraphs a giant beam at the player's current
    /// position (telegraph fades in over <see cref="sweepingBeamTelegraphTime"/>),
    /// then fires a fat death beam that slowly rotates to follow the player
    /// for <see cref="sweepingBeamFireDuration"/> seconds, dealing
    /// <see cref="sweepingBeamDamagePerSecond"/> while the player is on it.
    /// Used both as the boss's opening attack (right after the spawn slam)
    /// and as a third entry in the ranged-pattern rotation.
    /// </summary>
    private IEnumerator SweepingBeamPattern()
    {
        // Suppress the continuous bonus volleys while this pattern runs —
        // SweepingBeamPattern already starts its own grid volley coroutine
        // in parallel.
        isAerialOrSweepingActive = true;
        if (player == null) player = Hero.Instance;
        rb.velocity = Vector3.zero;

        // Fire the telegraphed grid volleys in PARALLEL with the sweeping
        // beam so every "big laser" attack ships with the wall-of-arrows
        // accompaniment, not just the spawn-attack one. The grid coroutine
        // self-cleans (telegraphs + arrows fire and despawn naturally), so
        // we deliberately don't store/Stop the handle — let it run its
        // course independently.
        if (spawnGridVolleysEnabled && aerialArrowPrefab != null && spawnGridVolleyCount > 0)
            StartCoroutine(SpawnAttackGridVolleys());

        // Dedicated LineRenderer for this attack so it doesn't fight with the
        // constant Death Beam routine for the same line.
        GameObject go = new GameObject("SweepingBeam", typeof(LineRenderer));
        go.transform.SetParent(transform, false);
        var lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.material = GetTelegraphMaterial();
        lr.startWidth = sweepingBeamTelegraphWidth;
        lr.endWidth   = sweepingBeamTelegraphWidth;
        Color startCol = sweepingBeamTelegraphColor; startCol.a = 0f;
        lr.startColor = startCol;
        lr.endColor   = startCol;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // ---- Telegraph phase ----
        // Tracks the player while alpha lerps 0→1. Wide and obvious so the
        // player can see the sweep coming.
        float t = 0f;
        Vector3 currentDir = transform.forward;
        while (t < sweepingBeamTelegraphTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / sweepingBeamTelegraphTime);
            Color c = sweepingBeamTelegraphColor; c.a = k;
            lr.startColor = c;
            lr.endColor   = c;

            Vector3 origin = transform.position + Vector3.up * beamOriginHeight;
            Vector3 target = (player != null && !player.IsDead)
                ? player.transform.position + Vector3.up * beamTargetHeight
                : origin + transform.forward;
            Vector3 dir = (target - origin);
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            currentDir = dir.normalized;
            Vector3 endpoint = origin + currentDir * sweepingBeamRange;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, endpoint);
            yield return null;
            if (!IsActive) { Destroy(go); isAerialOrSweepingActive = false; yield break; }
        }

        // ---- Lock phase ----
        // Telegraph stops tracking and stays at alpha 1 in a fixed direction
        // for sweepingBeamLockTime seconds. This is the dodge window — the
        // player can see exactly where the giant beam will start and step out.
        Color lockedCol = sweepingBeamTelegraphColor; lockedCol.a = 1f;
        lr.startColor = lockedCol;
        lr.endColor   = lockedCol;
        Vector3 lockOriginEarly = transform.position + Vector3.up * beamOriginHeight;
        Vector3 lockEndEarly    = lockOriginEarly + currentDir * sweepingBeamRange;
        lr.SetPosition(0, lockOriginEarly);
        lr.SetPosition(1, lockEndEarly);
        float lockT = 0f;
        while (lockT < sweepingBeamLockTime)
        {
            lockT += Time.deltaTime;
            // Keep the line glued to the boss in case the boss is somehow
            // displaced, but DO NOT update currentDir — the direction is locked.
            Vector3 origin = transform.position + Vector3.up * beamOriginHeight;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, origin + currentDir * sweepingBeamRange);
            yield return null;
            if (!IsActive) { Destroy(go); isAerialOrSweepingActive = false; yield break; }
        }

        // ---- Fire phase ----
        // Switch to the bow death-beam visual (so it gets the same bloom as
        // the constant beam) — fall back to the LineRenderer if no prefab is
        // assigned. Either way, slowly rotate currentDir toward the player
        // at a capped turn rate while damage ticks accumulate.
        ShakeCamera(sweepingBeamShakeAmplitude, sweepingBeamShakeDuration);

        GameObject sweepVisual = null;
        if (deathBeamVisualPrefab != null)
        {
            // Hide the telegraph LineRenderer; the bow visual takes over.
            lr.enabled = false;
            sweepVisual = Instantiate(deathBeamVisualPrefab);
            // Register so rage phase can destroy this visual if it interrupts
            // the fire phase mid-coroutine.
            activeBeamVisuals.Add(sweepVisual);
            // Strip the bow's DeathBeam script so the instance doesn't
            // self-destruct on the first frame (it requires an owner Hero).
            foreach (var db in sweepVisual.GetComponentsInChildren<DeathBeam>())
                Destroy(db);
            // sweepingBeamFireWidth is the LineRenderer thickness used as the
            // diameter for the default cube visual. Helper picks the right
            // scaling convention (FF lasers self-scale via length_multiplier).
            SetupBeamVisualScale(sweepVisual, sweepingBeamFireWidth, sweepingBeamRange);
        }
        else
        {
            // No prefab assigned — keep the LineRenderer styling.
            lr.startWidth = sweepingBeamFireWidth;
            lr.endWidth   = sweepingBeamFireWidth;
            Color fireCol = sweepingBeamFireColor; fireCol.a = 1f;
            lr.startColor = fireCol;
            lr.endColor   = fireCol;
        }

        // Spawn the lightning aura chain along the sweeping beam.
        {
            Vector3 spawnOrigin = transform.position + Vector3.up * beamOriginHeight;
            SpawnBeamAuras(spawnOrigin, currentDir, sweepingBeamRange);
        }

        float damageTimer = 0f;
        t = 0f;
        while (t < sweepingBeamFireDuration)
        {
            t += Time.deltaTime;
            damageTimer += Time.deltaTime;

            Vector3 origin = transform.position + Vector3.up * beamOriginHeight;
            // Rotate currentDir toward the player. Two stacked multipliers:
            //   1. Distance scaling: close = barely tracks, far = fast.
            //   2. Off-axis fast-track (>= 30° off): multiplies on top so the
            //      beam can snap when the player has moved hard off-axis.
            if (player != null && !player.IsDead)
            {
                Vector3 playerPos = player.transform.position;
                Vector3 target = playerPos + Vector3.up * beamTargetHeight;
                Vector3 desired = (target - origin);
                if (desired.sqrMagnitude > 0.0001f)
                {
                    desired.Normalize();

                    // Distance from boss (XZ plane) to player.
                    Vector3 flat = playerPos - transform.position;
                    flat.y = 0f;
                    float dist = flat.magnitude;
                    float distT = sweepingBeamFarDistance > sweepingBeamCloseDistance
                        ? Mathf.Clamp01((dist - sweepingBeamCloseDistance) /
                                        (sweepingBeamFarDistance - sweepingBeamCloseDistance))
                        : 1f;
                    float distMul = Mathf.Lerp(sweepingBeamCloseTrackMultiplier,
                                               sweepingBeamFarTrackMultiplier, distT);

                    float scaledBase  = sweepingBeamTrackDegPerSec * Mathf.Max(0f, distMul);
                    float effectiveRate = GetBeamTrackRate(scaledBase, currentDir, desired);
                    float maxRad = effectiveRate * Mathf.Deg2Rad * Time.deltaTime;
                    currentDir = Vector3.RotateTowards(currentDir, desired, maxRad, 0f).normalized;
                }
            }
            Vector3 endpoint = origin + currentDir * sweepingBeamRange;

            // Update visual.
            if (sweepVisual != null)
            {
                OrientBeamVisual(sweepVisual, origin, currentDir, sweepingBeamRange);
            }
            else
            {
                lr.SetPosition(0, origin);
                lr.SetPosition(1, endpoint);
            }

            // Re-place the aura chain to track the beam's current direction.
            UpdateBeamAuras(origin, currentDir, sweepingBeamRange);

            // Apply damage in fixed ticks so DPS stays consistent.
            if (damageTimer >= sweepingBeamDamageTickInterval && player != null && !player.IsDead)
            {
                Vector3 lineDir = (endpoint - origin);
                float lineLen = lineDir.magnitude;
                if (lineLen > 0.001f)
                {
                    Vector3 lineNorm = lineDir / lineLen;
                    Vector3 toPlayer = (player.transform.position + Vector3.up * beamTargetHeight) - origin;
                    float along = Mathf.Clamp(Vector3.Dot(toPlayer, lineNorm), 0f, lineLen);
                    Vector3 closest = origin + lineNorm * along;
                    float perpDist = Vector3.Distance(closest, player.transform.position + Vector3.up * beamTargetHeight);
                    if (perpDist <= sweepingBeamHitRadius)
                        player.TakeDamage(ScaledDamage(sweepingBeamDamagePerSecond) * damageTimer);
                }
                damageTimer = 0f;
            }

            yield return null;
            if (!IsActive)
            {
                if (sweepVisual != null) { activeBeamVisuals.Remove(sweepVisual); Destroy(sweepVisual); }
                ClearBeamAuras();
                Destroy(go);
                isAerialOrSweepingActive = false;
                yield break;
            }
        }

        if (sweepVisual != null) { activeBeamVisuals.Remove(sweepVisual); Destroy(sweepVisual); }
        ClearBeamAuras();
        Destroy(go);
        isAerialOrSweepingActive = false;
    }

    // -------------------- Constant attack: Death Beam --------------------

    /// <summary>
    /// Runs in parallel with the master melee/ranged state machine for the
    /// entire fight. Each cycle:
    ///   1. Wait <see cref="beamCooldown"/> seconds.
    ///   2. Fade-in phase: telegraph line fades alpha 0 → 1 over
    ///      <see cref="beamFadeInTime"/> seconds while continuously updating
    ///      its endpoint to the player's position. The fade reaching 1 is the
    ///      visual cue that the beam is about to lock.
    ///   3. Lock phase: telegraph stays at alpha 1 with a fixed endpoint for
    ///      <see cref="beamLockTime"/> seconds — this is the dodge window.
    ///   4. Fire phase: spawn the bow-style death-beam visual (or a thick
    ///      LineRenderer fallback) starting from the locked direction. The
    ///      beam slowly rotates toward the player at
    ///      <see cref="beamFireTrackDegPerSec"/> for
    ///      <see cref="beamFireDuration"/> seconds, dealing
    ///      <see cref="beamDamagePerSecond"/> while the player is within
    ///      <see cref="beamHitRadius"/> of its line.
    /// </summary>
    private IEnumerator DeathBeamRoutine()
    {
        yield return null; // wait one frame so spawnAttack starts first
        EnsureBeamLine();

        while (IsActive)
        {
            // Stay quiet during the spawn attack so the boss intro reads cleanly.
            if (!SpawnAttackResolved)
            {
                yield return null;
                continue;
            }

            // -- Cooldown (telegraph hidden) --
            if (beamLine != null) beamLine.enabled = false;
            yield return new WaitForSeconds(ScaledInterval(beamCooldown));
            if (!IsActive) yield break;

            if (player == null) player = Hero.Instance;
            if (player == null || player.IsDead) continue;

            // -- Fade-in phase: alpha 0→1 while tracking the player --
            EnsureBeamLine();
            beamLine.enabled = true;
            beamLine.startWidth = beamTelegraphWidth;
            beamLine.endWidth   = beamTelegraphWidth;

            float t = 0f;
            Vector3 lockedEnd = transform.position;
            while (t < beamFadeInTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / beamFadeInTime);
                Color c = new Color(beamTelegraphColor.r, beamTelegraphColor.g, beamTelegraphColor.b, k);
                beamLine.startColor = c;
                beamLine.endColor   = c;

                Vector3 origin = transform.position + Vector3.up * beamOriginHeight;
                Vector3 target = (player != null && !player.IsDead)
                    ? player.transform.position + Vector3.up * beamTargetHeight
                    : origin + transform.forward;
                Vector3 dir = (target - origin);
                if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
                dir.Normalize();
                Vector3 end = origin + dir * beamRange;
                beamLine.SetPosition(0, origin);
                beamLine.SetPosition(1, end);
                lockedEnd = end;
                yield return null;
                if (!IsActive) yield break;
            }

            // -- Lock phase: alpha = 1, DIRECTION frozen, but the line origin
            // (and therefore the endpoint) follows the boss every frame so
            // the telegraph stays glued to the boss when it dashes.
            Color full = new Color(beamTelegraphColor.r, beamTelegraphColor.g, beamTelegraphColor.b, 1f);
            beamLine.startColor = full;
            beamLine.endColor   = full;
            // Convert lockedEnd into a locked DIRECTION so we can re-derive
            // endpoint from the boss's current position each frame.
            Vector3 lockedDir = (lockedEnd - (transform.position + Vector3.up * beamOriginHeight));
            if (lockedDir.sqrMagnitude < 0.0001f) lockedDir = transform.forward;
            lockedDir.Normalize();
            float lockT = 0f;
            while (lockT < beamLockTime)
            {
                lockT += Time.deltaTime;
                Vector3 origin = transform.position + Vector3.up * beamOriginHeight;
                beamLine.SetPosition(0, origin);
                beamLine.SetPosition(1, origin + lockedDir * beamRange);
                yield return null;
                if (!IsActive) yield break;
            }
            // Update lockedEnd so the fire phase starts from where the lock
            // phase ended (in case the boss moved during the lock).
            Vector3 lockedOrigin = transform.position + Vector3.up * beamOriginHeight;
            lockedEnd = lockedOrigin + lockedDir * beamRange;

            // -- Fire phase: tracking sweep with continuous damage --
            // The beam starts pointing along the LOCKED direction (from the
            // end of the lock phase) and slowly rotates toward the player at
            // beamFireTrackDegPerSec. This means a still player gets cooked
            // but a moving player can outrun the rotation.
            ShakeCamera(beamShakeAmplitude, beamShakeDuration);

            Vector3 fireDir = (lockedEnd - lockedOrigin);
            if (fireDir.sqrMagnitude < 0.0001f) fireDir = transform.forward;
            fireDir.Normalize();

            // Configure the visual: prefer the assigned bow Death Beam visual;
            // fall back to the LineRenderer styling.
            GameObject beamVisual = null;
            if (deathBeamVisualPrefab != null)
            {
                beamLine.enabled = false;
                beamVisual = Instantiate(deathBeamVisualPrefab);
                // Register so rage phase can destroy this visual if it
                // interrupts the fire phase mid-coroutine.
                activeBeamVisuals.Add(beamVisual);
                // The bow's DeathBeam script self-destructs when its owner is
                // null, so strip it from the instance — we drive position /
                // rotation / damage from the SlimeGod coroutine instead.
                foreach (var db in beamVisual.GetComponentsInChildren<DeathBeam>())
                    Destroy(db);
                // Scale convention is prefab-dependent; helper handles both
                // the default cube-along-Z and the Flashy Feather +X convention.
                SetupBeamVisualScale(beamVisual, beamFireVisualRadius * 2f, beamRange);
            }
            else
            {
                beamLine.startWidth = beamFireWidth;
                beamLine.endWidth   = beamFireWidth;
                Color fc = beamFireColor; fc.a = 1f;
                beamLine.startColor = fc;
                beamLine.endColor   = fc;
            }

            // Spawn the lightning aura chain along the beam. Initial placement
            // uses the locked-fire direction; per-frame UpdateBeamAuras keeps
            // them tracking as the beam rotates toward the player.
            {
                Vector3 spawnOrigin = transform.position + Vector3.up * beamOriginHeight;
                SpawnBeamAuras(spawnOrigin, fireDir, beamRange);
            }

            float fireT = 0f;
            float damageTickT = 0f;
            while (fireT < beamFireDuration)
            {
                fireT += Time.deltaTime;
                damageTickT += Time.deltaTime;

                Vector3 origin = transform.position + Vector3.up * beamOriginHeight;

                // Rotate the beam direction toward the player, capped — but if
                // the player has gone past Beam Fast Track Threshold Degrees off
                // axis (e.g. dashed around behind the boss), the rotation
                // multiplies up so the beam catches back up.
                if (player != null && !player.IsDead)
                {
                    Vector3 desired = (player.transform.position + Vector3.up * beamTargetHeight) - origin;
                    if (desired.sqrMagnitude > 0.0001f)
                    {
                        desired.Normalize();
                        float effectiveRate = GetBeamTrackRate(beamFireTrackDegPerSec, fireDir, desired);
                        float maxRad = effectiveRate * Mathf.Deg2Rad * Time.deltaTime;
                        fireDir = Vector3.RotateTowards(fireDir, desired, maxRad, 0f).normalized;
                    }
                }
                Vector3 endpoint = origin + fireDir * beamRange;

                // Update visual.
                if (beamVisual != null)
                {
                    // Helper picks the right convention (centered cube vs FF +X anchored at origin).
                    OrientBeamVisual(beamVisual, origin, fireDir, beamRange);
                }
                else
                {
                    beamLine.SetPosition(0, origin);
                    beamLine.SetPosition(1, endpoint);
                }

                // Re-place the aura chain to track the beam's current direction.
                UpdateBeamAuras(origin, fireDir, beamRange);

                // Damage tick — point-line distance to the current beam.
                if (damageTickT >= beamDamageTickInterval && player != null && !player.IsDead)
                {
                    Vector3 toPlayer = (player.transform.position + Vector3.up * beamTargetHeight) - origin;
                    float along = Mathf.Clamp(Vector3.Dot(toPlayer, fireDir), 0f, beamRange);
                    Vector3 closest = origin + fireDir * along;
                    float perpDist = Vector3.Distance(closest, player.transform.position + Vector3.up * beamTargetHeight);
                    if (perpDist <= beamHitRadius)
                        player.TakeDamage(ScaledDamage(beamDamagePerSecond) * damageTickT);
                    damageTickT = 0f;
                }

                yield return null;
                if (!IsActive)
                {
                    if (beamVisual != null) { activeBeamVisuals.Remove(beamVisual); Destroy(beamVisual); }
                    ClearBeamAuras();
                    yield break;
                }
            }

            if (beamVisual != null) { activeBeamVisuals.Remove(beamVisual); Destroy(beamVisual); }
            ClearBeamAuras();
            beamLine.enabled = false;
        }
    }

    private void EnsureBeamLine()
    {
        if (beamLine != null) return;
        GameObject go = new GameObject("DeathBeam", typeof(LineRenderer));
        go.transform.SetParent(transform, false);
        beamLine = go.GetComponent<LineRenderer>();
        beamLine.useWorldSpace = true;
        beamLine.positionCount = 2;
        beamLine.startWidth = beamTelegraphWidth;
        beamLine.endWidth   = beamTelegraphWidth;
        beamLine.material = GetTelegraphMaterial();
        beamLine.startColor = new Color(beamTelegraphColor.r, beamTelegraphColor.g, beamTelegraphColor.b, 0f);
        beamLine.endColor   = new Color(beamTelegraphColor.r, beamTelegraphColor.g, beamTelegraphColor.b, 0f);
        beamLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamLine.receiveShadows = false;
        beamLine.enabled = false;
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// Returns the effective tracking rate for a death beam given a base
    /// rate and the current angle between the beam's direction and the
    /// desired direction. If the angle is at or above the fast-track
    /// threshold, the base rate is multiplied by Beam Fast Track Multiplier
    /// — letting the beam snap back when the player has moved far off-axis
    /// while staying slow/dodgeable when on-axis.
    /// </summary>
    private float GetBeamTrackRate(float baseRate, Vector3 currentDir, Vector3 desiredDir)
    {
        if (currentDir.sqrMagnitude < 0.0001f || desiredDir.sqrMagnitude < 0.0001f) return baseRate;
        float ang = Vector3.Angle(currentDir, desiredDir);
        if (ang >= beamFastTrackThresholdDegrees) return baseRate * Mathf.Max(1f, beamFastTrackMultiplier);
        return baseRate;
    }

    /// <summary>
    /// Apply a uniform visual scale to a spawned EnemyProjectile while
    /// inversely re-scaling its trigger collider(s) so the hitbox stays the
    /// same world-space size as on the prefab. Without this, scaling the
    /// transform shrinks the collider too, which is why small arrows
    /// frequently miss the player capsule.
    /// </summary>
    private static void ApplyArrowScale(EnemyProjectile p, float scale)
    {
        if (p == null) return;
        if (scale <= 0f || Mathf.Abs(scale - 1f) < 0.001f) return;
        p.transform.localScale *= scale;
        // Inverse-scale every collider on the projectile so the trigger
        // volume keeps its original world-space size.
        float inv = 1f / scale;
        foreach (var col in p.GetComponentsInChildren<Collider>(true))
        {
            switch (col)
            {
                case BoxCollider bx:     bx.size   *= inv;                       break;
                case SphereCollider sp:  sp.radius *= inv;                       break;
                case CapsuleCollider cp: cp.radius *= inv; cp.height *= inv;     break;
                // Other collider types (mesh, terrain) are left alone — they
                // don't matter for projectile hit detection in this game.
            }
        }
    }

    /// <summary>Convenience wrapper around CameraFollow.Shake. No-op if no main camera or follow component.</summary>
    private static void ShakeCamera(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f) return;
        if (Camera.main == null) return;
        var cf = Camera.main.GetComponent<CameraFollow>();
        if (cf != null) cf.Shake(amplitude, duration);
    }

    /// <summary>
    /// Clamp a candidate position to the arena bounds (XZ only) so the boss
    /// can't dash / bounce / drop outside the map. Lazily caches the
    /// ArenaGenerator on first use. Y is preserved.
    /// </summary>
    private Vector3 ClampToArena(Vector3 pos)
    {
        if (cachedArena == null) cachedArena = FindObjectOfType<ArenaGenerator>();
        if (cachedArena == null) return pos;
        float half = cachedArena.arenaSize * 0.5f - Mathf.Max(0f, arenaEdgeMargin);
        if (half <= 0f) return pos;
        pos.x = Mathf.Clamp(pos.x, -half, half);
        pos.z = Mathf.Clamp(pos.z, -half, half);
        return pos;
    }

    private Vector3 PredictPlayerPosition(float seconds)
    {
        if (player == null) return transform.position;
        Vector3 p = player.transform.position + estPlayerVel * seconds;
        p.y = transform.position.y;
        return p;
    }

    private LineRenderer SpawnTelegraphLine(Vector3 a, Vector3 b, Color color)
        => SpawnTelegraphLine(a, b, color, telegraphLineWidth);

    private LineRenderer SpawnTelegraphLine(Vector3 a, Vector3 b, Color color, float width)
    {
        GameObject go = new GameObject("BossTelegraph", typeof(LineRenderer));
        var lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.startWidth = width;
        lr.endWidth   = width;
        lr.material = GetTelegraphMaterial();
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        // Register so ClearAllTelegraphs() catches orphans from cancelled
        // patterns (the aerial volley's telegraph fan otherwise floats
        // permanently if the master coroutine is stopped mid-telegraph).
        transientTelegraphPool.Add(lr);
        return lr;
    }

    private GameObject MakeGroundRing(Vector3 center, float radius, Color color)
    {
        GameObject go = new GameObject("BossGroundRing", typeof(LineRenderer));
        go.transform.position = center + Vector3.up * 0.05f;
        var lr = go.GetComponent<LineRenderer>();
        const int seg = 36;
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = seg;
        lr.startWidth = telegraphLineWidth * 0.9f;
        lr.endWidth   = telegraphLineWidth * 0.9f;
        lr.material = GetTelegraphMaterial();
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        for (int i = 0; i < seg; i++)
        {
            float a = (360f / seg) * i * Mathf.Deg2Rad;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        return go;
    }

    private void ClearDashTelegraphs()
    {
        for (int i = 0; i < dashTelegraphPool.Count; i++)
            if (dashTelegraphPool[i] != null) Destroy(dashTelegraphPool[i].gameObject);
        dashTelegraphPool.Clear();
    }

    private void ClearBounceTelegraphs()
    {
        for (int i = 0; i < bounceTelegraphPool.Count; i++)
            if (bounceTelegraphPool[i] != null) Destroy(bounceTelegraphPool[i]);
        bounceTelegraphPool.Clear();
    }

    private void ClearTransientTelegraphs()
    {
        for (int i = 0; i < transientTelegraphPool.Count; i++)
            if (transientTelegraphPool[i] != null) Destroy(transientTelegraphPool[i].gameObject);
        transientTelegraphPool.Clear();
    }

    private void ClearAllTelegraphs()
    {
        ClearDashTelegraphs();
        ClearBounceTelegraphs();
        ClearTransientTelegraphs();
    }

    private void EnsureShieldVisual()
    {
        if (shieldVisual != null) return;
        shieldVisual = new GameObject("BossShield");
        shieldVisual.transform.SetParent(transform, false);
        shieldVisual.transform.localPosition = Vector3.up * 1.5f;
        var l = shieldVisual.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = shieldColor;
        l.intensity = 6f;
        l.range = 6f;
        l.shadows = LightShadows.None;
    }

    private void DestroyShieldVisual()
    {
        if (shieldVisual != null) Destroy(shieldVisual);
        shieldVisual = null;
    }

    private static Material cachedTelegraphMat;
    private static Material GetTelegraphMaterial()
    {
        if (cachedTelegraphMat != null) return cachedTelegraphMat;
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        cachedTelegraphMat = new Material(s);
        cachedTelegraphMat.hideFlags = HideFlags.HideAndDontSave;
        return cachedTelegraphMat;
    }
}
