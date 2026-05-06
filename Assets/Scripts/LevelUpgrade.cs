using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One picker option in the LevelUpChoiceUI. Subclasses encode the actual
/// stat / behavior change that gets applied when the player selects this
/// option from the level-up menu.
///
/// The level-up system is intentionally separate from the per-pickup
/// powerup system (PowerUpChoiceUI / Hero.ApplyPowerUp): level-up upgrades
/// are scarcer and bigger — they typically change playstyle rather than
/// nudge a stat — and they're slotted by source (equipped weapon, other
/// weapon, general buff) instead of by pickup type.
///
/// Slot fitting:
///   * <see cref="Slot.EquippedWeapon"/> — picked when the upgrade applies
///     to a weapon currently in primary OR secondary.
///   * <see cref="Slot.OtherWeapon"/>    — picked when it applies to a
///     weapon NOT currently equipped (lets the player preview / unlock
///     content for weapons they haven't picked up yet).
///   * <see cref="Slot.GeneralBuff"/>    — slot 3, no weapon association.
///     Per the spec, slot 3 disappears after the player picks any general
///     buff (the buff also unlocks an MMB-bound active ability).
/// </summary>
public abstract class LevelUpgrade
{
    public enum Slot
    {
        EquippedWeapon = 0,
        OtherWeapon    = 1,
        GeneralBuff    = 2,
    }

    /// <summary>Short name shown at the top of the panel.</summary>
    public string DisplayName { get; protected set; }

    /// <summary>Body text describing what the upgrade does.</summary>
    public string Description { get; protected set; }

    /// <summary>Optional icon. Null falls back to a placeholder square in the UI.</summary>
    public Sprite Icon { get; protected set; }

    /// <summary>Which UI slot this upgrade is allowed to populate.</summary>
    public Slot UpgradeSlot { get; protected set; }

    /// <summary>
    /// For weapon-specific upgrades, the weapon type this upgrade targets.
    /// Used by the slot-fitting logic to decide whether the upgrade goes to
    /// the EquippedWeapon slot or the OtherWeapon slot at offering time.
    /// Ignored for <see cref="Slot.GeneralBuff"/>.
    /// </summary>
    public eWeaponType TargetWeapon { get; protected set; } = eWeaponType.none;

    /// <summary>
    /// Returns true if this upgrade is currently eligible to be offered.
    /// Used to gate upgrades behind unlock conditions (e.g. Zenith requires
    /// the player to already have it unlocked) and to prevent picking the
    /// same one-shot upgrade twice.
    /// </summary>
    public abstract bool IsAvailable(Hero hero);

    /// <summary>
    /// Apply the upgrade to the hero. Called once when the player clicks
    /// the panel's Select button. Implementations should mark themselves as
    /// "taken" if they shouldn't be offered again (e.g. by setting a flag
    /// on the hero or in <see cref="LevelUpgradeRegistry"/>).
    /// </summary>
    public abstract void Apply(Hero hero);
}

/// <summary>
/// Run-scoped catalog of every level-up upgrade. The LevelUpChoiceUI queries
/// this when a level-up fires to decide which three options to offer.
///
/// This is a plain runtime container, not a singleton — it lives alongside
/// the GameManager and is rebuilt per run. Upgrades register themselves in
/// <see cref="Initialize"/> by adding to the public lists. Phase 1 keeps the
/// catalog mostly empty (just a couple of placeholder upgrades for UI
/// testing); real upgrades land in phases 2–4.
/// </summary>
public class LevelUpgradeRegistry
{
    public static LevelUpgradeRegistry Instance { get; private set; }

    /// <summary>Every weapon-targeted upgrade in the run, regardless of equip state. Slot fitting picks at offer time.</summary>
    public readonly List<LevelUpgrade> WeaponUpgrades = new List<LevelUpgrade>();
    /// <summary>Every general (non-weapon) upgrade in the run. Slot 3 will be hidden once the player picks one of these.</summary>
    public readonly List<LevelUpgrade> GeneralUpgrades = new List<LevelUpgrade>();

    /// <summary>True once any general buff has been chosen this run.</summary>
    public bool GeneralBuffChosen { get; private set; }

    /// <summary>Called by the buff itself when applied so the registry knows the slot is locked out.</summary>
    public void NotifyGeneralBuffChosen() { GeneralBuffChosen = true; }

    /// <summary>
    /// Rebuild the catalog for a new run. Called by GameManager (or the
    /// level system) on first use. Subclasses of <see cref="LevelUpgrade"/>
    /// register themselves here.
    /// </summary>
    public static LevelUpgradeRegistry Initialize()
    {
        Instance = new LevelUpgradeRegistry();
        Instance.GeneralUpgrades.Add(new IAmTankUpgrade());
        Instance.GeneralUpgrades.Add(new MeteorUpgrade());
        Instance.WeaponUpgrades.Add(new SwordZenithUpgrade());
        return Instance;
    }

    /// <summary>
    /// Pick the three offerings for a single level-up. Returns up to three
    /// upgrades, one per slot. Returns nulls in slots where no upgrade was
    /// eligible — the UI hides those panels per the user-chosen behavior.
    /// </summary>
    public LevelUpgrade[] RollOffer(Hero hero)
    {
        var equippedSlot = PickWeaponUpgrade(hero, mustBeEquipped: true);
        var otherSlot    = PickWeaponUpgrade(hero, mustBeEquipped: false, exclude: equippedSlot);
        var generalSlot  = GeneralBuffChosen ? null : PickGeneralUpgrade(hero);
        return new[] { equippedSlot, otherSlot, generalSlot };
    }

    private LevelUpgrade PickWeaponUpgrade(Hero hero, bool mustBeEquipped, LevelUpgrade exclude = null)
    {
        if (hero == null) return null;
        var pool = new List<LevelUpgrade>();
        for (int i = 0; i < WeaponUpgrades.Count; i++)
        {
            var u = WeaponUpgrades[i];
            if (u == null || u == exclude) continue;
            if (!u.IsAvailable(hero)) continue;
            bool isEquipped = HeroHasWeaponEquipped(hero, u.TargetWeapon);
            if (mustBeEquipped != isEquipped) continue;
            pool.Add(u);
        }
        return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
    }

    private LevelUpgrade PickGeneralUpgrade(Hero hero)
    {
        if (hero == null) return null;
        var pool = new List<LevelUpgrade>();
        for (int i = 0; i < GeneralUpgrades.Count; i++)
        {
            var u = GeneralUpgrades[i];
            if (u == null) continue;
            if (!u.IsAvailable(hero)) continue;
            pool.Add(u);
        }
        return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
    }

    private static bool HeroHasWeaponEquipped(Hero hero, eWeaponType type)
    {
        if (hero == null || type == eWeaponType.none) return false;
        if (hero.primaryWeapon != null && hero.primaryWeapon.weaponType == type) return true;
        if (hero.secondaryWeapon != null && hero.secondaryWeapon.weaponType == type) return true;
        return false;
    }
}

// ============================================================================
//                          Concrete upgrades
// ============================================================================

/// <summary>
/// "I am Tank!" — slot-3 general buff. Granted at level-up; locks slot 3 for
/// the rest of the run. Switches HP / regen scaling to fit a tank fantasy
/// and unlocks an MMB-bound shield ability.
///
/// Effect (all driven by Hero.iAmTankActive):
///   * Max HP boosts become exponential (×1.05 per pickup) instead of flat +10.
///   * Health regen rate AND each regen-boost gain are doubled.
///   * Middle Mouse Button cast: heal 20% MaxHP, spawn a one-hit-absorb shield
///     that lasts 35s, grant +30% damage while the shield exists.
///   * Cooldown 30s; recasting in the last few seconds before expiry refreshes
///     the duration without breaking the +30% buff continuity.
/// </summary>
public class IAmTankUpgrade : LevelUpgrade
{
    public IAmTankUpgrade()
    {
        DisplayName = "I am Tank!";
        Description =
            "HP upgrades scale exponentially (+5% MaxHP per pickup).\n" +
            "Health regen and regen upgrades have doubled effect.\n\n" +
            "MIDDLE MOUSE BUTTON: heal 20% MaxHP and gain a shield that absorbs the next hit. " +
            "While the shield is up, +30% damage. Shield lasts 35s. Cooldown 30s.";
        UpgradeSlot = Slot.GeneralBuff;
        TargetWeapon = eWeaponType.none;
    }

    public override bool IsAvailable(Hero hero)
    {
        if (hero == null) return false;
        // One-shot — once active, this upgrade never re-appears in the registry.
        if (hero.iAmTankActive) return false;
        // Also gated by the registry's per-run "general buff already chosen" flag,
        // so the slot-3 panel disappears entirely after any general buff is taken.
        return true;
    }

    public override void Apply(Hero hero)
    {
        if (hero == null) return;
        hero.iAmTankActive = true;
        // Reset cooldown so the player can use the ability immediately on
        // first acquisition (otherwise they'd need to wait the default 30s).
        // Implementation note: we don't have direct write access to the
        // private cooldown timer, so the timer's already-zero default state
        // means casting works the moment this returns.
        if (LevelUpgradeRegistry.Instance != null)
            LevelUpgradeRegistry.Instance.NotifyGeneralBuffChosen();
        Debug.Log("[Level-Up] I am Tank! activated.");
    }
}

/// <summary>
/// "Zenith" — sword level-up upgrade. Picking this UNLOCKS Zenith but does
/// NOT immediately apply it. Instead, after the sword's slash arc maxes at
/// 360°, the next time the player picks up a Grenade powerup the sword's
/// regular grenade-pickup boost option is replaced with "Apply Zenith" in
/// the powerup choice UI. Choosing that option triggers the actual
/// transformation: dual-wield, elliptical 360° sweep, rainbow trail,
/// doubled damage.
///
/// Slot fitting: this upgrade lives in the WeaponUpgrades pool with
/// TargetWeapon = sword. The registry's slot logic decides at offer time
/// whether to show it in the equipped-weapon slot (sword in primary or
/// secondary) or the other-weapon slot (sword not equipped — player can
/// preview the unlock before picking up a sword).
/// </summary>
public class SwordZenithUpgrade : LevelUpgrade
{
    public SwordZenithUpgrade()
    {
        DisplayName = "Zenith";
        Description = "The culmination of a journey\nProve yourself worthy of the end\n\nYou Will Suffer";
        UpgradeSlot = Slot.EquippedWeapon; // overridden by registry slot fitting based on equip
        TargetWeapon = eWeaponType.sword;
    }

    public override bool IsAvailable(Hero hero)
    {
        if (hero == null) return false;
        // Find the sword component on the hero (it persists whether equipped
        // or not). If unlocked or already applied, this upgrade is consumed.
        SwordWeapon sword = hero.GetWeaponComponentForType(eWeaponType.sword) as SwordWeapon;
        if (sword == null) return false;
        if (sword.zenithUnlocked || sword.zenithApplied) return false;
        return true;
    }

    public override void Apply(Hero hero)
    {
        if (hero == null) return;
        SwordWeapon sword = hero.GetWeaponComponentForType(eWeaponType.sword) as SwordWeapon;
        if (sword == null)
        {
            Debug.LogWarning("[Zenith] No SwordWeapon component on hero — Zenith unlock no-op.");
            return;
        }
        sword.zenithUnlocked = true;
        // Curse the player: replace BOTH equipped weapons with sword. The
        // curse stat modifiers (1/4 outgoing dmg, 2× incoming dmg, 50%
        // powerup efficiency, weapon-switch lockout) are gated by
        // Hero.IsZenithCursed, which reads sword.zenithUnlocked &&
        // !sword.zenithApplied — so simply flipping zenithUnlocked
        // activates the entire curse.
        if (hero.primaryWeapon != sword)
            hero.ReplaceWeaponSlot(replacePrimary: true,  newType: eWeaponType.sword);
        if (hero.secondaryWeapon != sword)
            hero.ReplaceWeaponSlot(replacePrimary: false, newType: eWeaponType.sword);
        Debug.Log("[Level-Up] Zenith UNLOCKED. The trial begins. You Will Suffer.");
    }
}

/// <summary>
/// "Meteor" — slot-3 general buff. Once accepted, every CRIT attack from any
/// weapon has a 50% chance to call down a meteor on the first enemy the
/// fire-event hits. The meteor falls in place at the target and deals 4×
/// that hit's damage as AOE on impact.
///
/// Hooks into the existing crit roll via Hero.ComputeAttackDamageWithCrit
/// (no duplicate roll), so the augment respects the player's crit rate and
/// crit damage stats — boosted crit chance ⇒ more meteors. Per-weapon
/// arming + first-hit consumption is wired through the generic
/// <see cref="MeteorArmer"/> component, so the augment scales to every
/// projectile / swing / throw without weapon-specific glue.
///
/// Slot 3 is consumed once any general buff is taken, so picking Meteor
/// also locks out future I-am-Tank offers (and vice-versa).
/// </summary>
public class MeteorUpgrade : LevelUpgrade
{
    public MeteorUpgrade()
    {
        DisplayName = "Meteor";
        Description =
            "Critical hits from ANY weapon have a 50% chance to call down a " +
            "meteor on the first enemy struck by that attack. The meteor deals " +
            "4× the attack's damage as AOE.";
        UpgradeSlot = Slot.GeneralBuff;
        TargetWeapon = eWeaponType.none;
    }

    public override bool IsAvailable(Hero hero)
    {
        if (hero == null) return false;
        // One-shot — once active, never re-offered.
        if (hero.meteorEnabled) return false;
        // Also gated by the registry's per-run "general buff already chosen"
        // flag (slot 3 is hidden once any general buff has been picked).
        return true;
    }

    public override void Apply(Hero hero)
    {
        if (hero == null) return;
        hero.meteorEnabled = true;
        if (LevelUpgradeRegistry.Instance != null)
            LevelUpgradeRegistry.Instance.NotifyGeneralBuffChosen();
        Debug.Log("[Level-Up] Meteor general augment activated.");
    }
}
