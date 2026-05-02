using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Powerup choice UI.
/// Opens when the Hero collects a PowerUp.
///
/// Two choices:
///   1. Upgrade current matching weapon.
///      If that weapon is already maxed, the Hero gets a stat boost instead.
///
///   2. Replace weapon.
///      If the powerup is already for one of the Hero's active weapons, this
///      button changes to the same upgrade/stat-boost text so the player is
///      not asked to replace a weapon with the same weapon they already have.
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
                "Choose whether to replace a weapon with it or take a Hero stat boost.";
        }
        else if (matchingWeapon.IsDamageMaxed)
        {
            descriptionText.text =
                matchingWeapon.weaponName + " is already maxed.\n" +
                "Choosing either option will increase a Hero stat instead.";
        }
        else
        {
            descriptionText.text =
                "You already have " + matchingWeapon.weaponName + ".\n" +
                "Choose either option to upgrade it.";
        }
    }

    private void SetupButtons()
    {
        Weapon matchingWeapon = hero.GetWeaponOfType(pendingType);

        string upgradeLabel = GetUpgradeLabel(matchingWeapon);

        if (upgradeButtonText != null)
            upgradeButtonText.text = upgradeLabel;

        if (replaceButtonText != null)
        {
            if (matchingWeapon != null)
                replaceButtonText.text = upgradeLabel;
            else
                replaceButtonText.text = "Replace Weapon With " + GetWeaponName(pendingType);
        }

        upgradeButton.onClick.RemoveAllListeners();
        replaceButton.onClick.RemoveAllListeners();

        upgradeButton.onClick.AddListener(() =>
        {
            ApplyUpgradeOrStatBoost(matchingWeapon);
            Close();
        });

        replaceButton.onClick.AddListener(() =>
        {
            if (matchingWeapon != null)
            {
                ApplyUpgradeOrStatBoost(matchingWeapon);
            }
            else
            {
                hero.BeginReplaceWeaponChoice(pendingType);
            }

            Close();
        });
    }

    private string GetUpgradeLabel(Weapon matchingWeapon)
    {
        if (matchingWeapon == null)
            return "Take Hero Stat Boost";

        if (matchingWeapon.IsDamageMaxed)
            return "Weapon Maxed: Boost Hero Stat";

        return "Upgrade " + matchingWeapon.weaponName;
    }

    private void ApplyUpgradeOrStatBoost(Weapon matchingWeapon)
    {
        if (matchingWeapon != null && !matchingWeapon.IsDamageMaxed)
            hero.UpgradeWeaponDamage(matchingWeapon);
        else
            hero.ApplyHeroStatBoost();
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
            case eWeaponType.sword:
                return "Sword";

            case eWeaponType.shield:
                return "Shield";

            case eWeaponType.bow:
                return "Bow";

            case eWeaponType.dagger:
                return "Dagger";

            case eWeaponType.crossbow:
                return "Crossbow";

            case eWeaponType.grenade:
                return "Grenade";

            default:
                return "Unknown";
        }
    }
}