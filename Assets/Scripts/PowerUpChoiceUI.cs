using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Two-button powerup choice UI.
/// Opens when the Hero collects a PowerUp and pauses the game.
///
/// Two choices:
///   1. Upgrade current matching weapon.
///      If the matching weapon is already maxed, this gives a Hero stat boost instead.
///      If the Hero does not have the matching weapon, this gives a Hero stat boost.
///
///   2. Replace weapon with this powerup's weapon.
///      Current behavior replaces the secondary weapon slot by default.
///      If the weapon component does not exist on the Hero, this gives a Hero stat boost instead.
///
/// Setup:
///   * Put this script on an always-enabled PowerUpUI GameObject.
///   * Drag ChoicePanel into Panel Root.
///   * Keep PowerUpUI enabled, but disable ChoicePanel at game start.
///   * Drag the two Button components and their TMP text components into the fields.
/// </summary>
public class PowerUpChoiceUI : MonoBehaviour
{
    public static PowerUpChoiceUI Instance { get; private set; }

    [Header("UI")]
    public GameObject panelRoot;

    public Button upgradeButton;
    public Button replaceButton;

    public TMP_Text upgradeButtonText;
    public TMP_Text replaceButtonText;

    public TMP_Text titleText;
    public TMP_Text descriptionText;

    private Hero hero;
    private eWeaponType pendingType;

    private void Awake()
    {
        Instance = this;

        if (panelRoot == null)
            panelRoot = gameObject;

        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show(Hero hero, eWeaponType type)
    {
        this.hero = hero;
        pendingType = type;

        Time.timeScale = 0f;
        panelRoot.SetActive(true);

        SetupText();
        SetupButtons();
    }

    private void SetupText()
    {
        string weaponName = GetWeaponName(pendingType);
        Weapon matchingWeapon = hero.GetWeaponOfType(pendingType);

        if (titleText != null)
            titleText.text = weaponName + " PowerUp";

        if (descriptionText == null)
            return;

        if (matchingWeapon == null)
        {
            descriptionText.text =
                "You do not currently have " + weaponName + ".\n" +
                "Choose whether to replace your secondary weapon with it or take a Hero stat boost.";
        }
        else if (matchingWeapon.IsDamageMaxed)
        {
            descriptionText.text =
                matchingWeapon.weaponName + " is already maxed.\n" +
                "Choosing upgrade will increase a Hero stat instead.";
        }
        else
        {
            descriptionText.text =
                "Choose whether to upgrade your current " + matchingWeapon.weaponName +
                " or replace your secondary weapon slot.";
        }
    }

    private void SetupButtons()
    {
        Weapon matchingWeapon = hero.GetWeaponOfType(pendingType);

        if (upgradeButtonText != null)
        {
            if (matchingWeapon == null)
                upgradeButtonText.text = "Take Hero Stat Boost";
            else if (matchingWeapon.IsDamageMaxed)
                upgradeButtonText.text = "Weapon Maxed: Boost Hero Stat";
            else
                upgradeButtonText.text = "Upgrade " + matchingWeapon.weaponName;
        }

        if (replaceButtonText != null)
            replaceButtonText.text = "Replace Weapon With " + GetWeaponName(pendingType);

        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(() =>
            {
                if (matchingWeapon != null && !matchingWeapon.IsDamageMaxed)
                    hero.UpgradeWeaponDamage(matchingWeapon);
                else
                    hero.ApplyHeroStatBoost();

                Close();
            });
        }

        if (replaceButton != null)
        {
            replaceButton.onClick.RemoveAllListeners();
            replaceButton.onClick.AddListener(() =>
            {
                hero.BeginReplaceWeaponChoice(pendingType);
                Close();
            });
        }
    }

    private void Close()
    {
        Hide();
        Time.timeScale = 1f;
    }

    private void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private string GetWeaponName(eWeaponType type)
    {
        switch (type)
        {
            case eWeaponType.sword: return "Sword";
            case eWeaponType.shield: return "Shield";
            case eWeaponType.bow: return "Bow";
            case eWeaponType.dagger: return "Dagger";
            case eWeaponType.crossbow: return "Crossbow";
            case eWeaponType.grenade: return "Grenade";
            default: return "Unknown";
        }
    }
}
