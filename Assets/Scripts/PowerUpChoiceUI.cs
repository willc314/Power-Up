using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Power-up choice UI shown when the Hero collects a PowerUp.
/// Built entirely from script so the scene only needs an empty GameObject
/// with this component on it.
///
/// Layout:
///   ┌──────────────────── dim background ───────────────────────┐
///   │                                                           │
///   │  ┌───────┐         Picked Up:           ┌───────┐         │
///   │  │ icon  │         [BIG icon]           │ icon  │         │
///   │  │ name  │           name               │ name  │         │
///   │  │       │                              │       │         │
///   │  │ Repl. │                              │ Equip │         │
///   │  │ Boost │                              │       │         │
///   │  └───────┘                              └───────┘         │
///   │   PRIMARY                                SECONDARY        │
///   │                                                           │
///   └───────────────────────────────────────────────────────────┘
///
/// Behavior per slot:
///   * Empty slot     →  single Equip button (puts the picked-up weapon there).
///   * Filled slot    →  Replace + Boost buttons.
///                       - Replace swaps that slot for the picked-up weapon.
///                       - Boost applies the slot weapon's intrinsic upgrade
///                         (Sword/Shield/Bow → +damage, Crossbow → +projectile,
///                         Grenade → +range, Dagger → +attack speed). The
///                         button text spells out exactly which stat changes.
///
/// Setup:
///   * Drop one GameObject named "PowerUpChoiceUI" in the scene with this component.
///   * No other wiring needed. (Any existing UI children are auto-hidden.)
/// </summary>
public class PowerUpChoiceUI : MonoBehaviour
{
    public static PowerUpChoiceUI Instance { get; private set; }

    /// <summary>
    /// The picked-up weapon type for the panel currently being shown (or
    /// last shown). Lets weapon overrides differentiate boost behavior by
    /// pickup source — e.g. SwordWeapon only grows its arc when the pickup
    /// was a Sword, falling back to standard +damage for Bow / Shield.
    /// </summary>
    public static eWeaponType LastPickupType { get; private set; } = eWeaponType.none;

    [Header("Style")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.55f);
    public Color panelColor = new Color(0.12f, 0.12f, 0.14f, 0.95f);
    public Color panelOutlineColor = new Color(1f, 1f, 1f, 0.18f);
    public Color buttonColor = new Color(0.22f, 0.22f, 0.26f, 1f);
    public Color buttonHoverColor = new Color(0.32f, 0.34f, 0.40f, 1f);
    public Color buttonPressedColor = new Color(0.16f, 0.16f, 0.20f, 1f);
    public Color textColor = Color.white;
    public Color subtleTextColor = new Color(1f, 1f, 1f, 0.65f);
    public Color emptySlotColor = new Color(0.18f, 0.18f, 0.20f, 0.85f);

    private Hero hero;
    private eWeaponType pendingType;
    // Backlog of pickup types collected while the choice panel was already
    // open. Drained one-by-one in Close(): each queued entry transitions the
    // panel to the next pickup instead of fully closing, so the player gets
    // to make a choice for every powerup they grabbed (no silent overwrite).
    private readonly Queue<eWeaponType> pendingQueue = new Queue<eWeaponType>();

    /// <summary>True while the choice panel is currently visible to the player.</summary>
    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    // Built UI references (rebuilt each Show so the layout matches state).
    private Canvas canvas;
    private GameObject panelRoot;
    private Text titleText;
    private Image centerIconImg;
    private Text centerNameText;
    private SlotWidgets primarySlot;
    private SlotWidgets secondarySlot;
    private Font defaultFont;

    private struct SlotWidgets
    {
        public GameObject root;
        public Text headerText;     // "PRIMARY" / "SECONDARY"
        public Image iconImg;
        public Text nameText;
        public GameObject equipButtonGO;
        public Button equipButton;
        public Text equipButtonText;
        public GameObject replaceButtonGO;
        public Button replaceButton;
        public Text replaceButtonText;
        public GameObject boostButtonGO;
        public Button boostButton;
        public Text boostButtonText;
        public GameObject heroStatButtonGO; // 3rd option — only shown when the weapon is at cap
        public Button heroStatButton;
        public Text heroStatButtonText;
    }

    private void Awake()
    {
        Instance = this;
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Hide any existing UI children left over from the previous version of
        // this script so they don't render on top of the procedural panel.
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null) child.gameObject.SetActive(false);
        }

        EnsureEventSystem();
        BuildUI();
        Hide();
    }

    private static void EnsureEventSystem()
    {
        // Buttons need an EventSystem somewhere in the scene to receive clicks.
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.hideFlags = HideFlags.DontSave;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // -------------------- Public entry point --------------------

    public void Show(Hero hero, eWeaponType type)
    {
        // Edge case: the hero walks into two (or more) powerups in the same
        // frame. Without this guard, the second Show() would overwrite
        // pendingType, voiding the first powerup's choice. Instead, queue the
        // extra pickups and drain them one-by-one in Close().
        if (IsOpen)
        {
            pendingQueue.Enqueue(type);
            return;
        }

        this.hero = hero;
        this.pendingType = type;

        Time.timeScale = 0f;
        panelRoot.SetActive(true);

        // Dim the music while the player is choosing a powerup. EndDuck pairs with this in Close().
        if (MusicManager.Instance != null) MusicManager.Instance.BeginDuck();

        Refresh();
    }

    private void Refresh()
    {
        // Publish which pickup type is currently being offered so weapon
        // overrides (e.g. SwordWeapon's arc growth) can read it.
        LastPickupType = pendingType;

        // --- Center: picked-up weapon name + icon ---
        Weapon pickedUpWeapon = hero != null ? hero.GetWeaponComponentForType(pendingType) : null;
        string pickedUpName = pickedUpWeapon != null ? pickedUpWeapon.weaponName : GetWeaponName(pendingType);
        Sprite pickedUpIcon = pickedUpWeapon != null ? pickedUpWeapon.hudIcon : null;

        // Stack on two lines so longer weapon names don't push past the
        // panel edges. Header on top, name centered underneath.
        if (titleText != null) titleText.text = "Picked Up:\n" + pickedUpName;

        if (centerIconImg != null)
        {
            centerIconImg.sprite = pickedUpIcon;
            centerIconImg.color = pickedUpIcon != null
                ? Color.white
                : new Color(1f, 1f, 1f, 0.12f);
        }
        if (centerNameText != null) centerNameText.text = pickedUpName;

        // --- Slots ---
        ConfigureSlot(primarySlot,   "PRIMARY",   0, hero != null ? hero.primaryWeapon   : null);
        ConfigureSlot(secondarySlot, "SECONDARY", 1, hero != null ? hero.secondaryWeapon : null);
    }

    private void ConfigureSlot(SlotWidgets slot, string header, int slotIndex, Weapon equipped)
    {
        if (slot.headerText != null) slot.headerText.text = header;

        // The picked-up weapon defines what kind of boost is on offer
        // (Bow/Sword/Shield → Damage, Crossbow → Projectiles,
        // Grenade → Range, Dagger → AttackSpeed). Each weapon below
        // translates that kind into its own appropriate stat change.
        BoostKind boostKind = Weapon.GetBoostKindForPickup(pendingType);

        // What's in the OTHER slot? Used to gray out actions that would put
        // the picked-up weapon next to a duplicate of itself.
        Weapon otherSlotWeapon = (slotIndex == 0) ? hero.secondaryWeapon : hero.primaryWeapon;
        bool otherHasSameType  = otherSlotWeapon != null && otherSlotWeapon.weaponType == pendingType;

        if (equipped == null)
        {
            // Empty slot: show placeholder + single Equip button.
            if (slot.iconImg != null)
            {
                slot.iconImg.sprite = null;
                slot.iconImg.color = emptySlotColor;
            }
            if (slot.nameText != null)
            {
                slot.nameText.text = "(empty)";
                slot.nameText.color = subtleTextColor;
            }

            slot.equipButtonGO.SetActive(true);
            slot.replaceButtonGO.SetActive(false);
            slot.boostButtonGO.SetActive(false);
            slot.heroStatButtonGO.SetActive(false);

            // Disallow equipping a duplicate of the weapon already in the
            // other slot — keeps the loadout to two distinct weapons.
            if (otherHasSameType)
            {
                slot.equipButtonText.text = "Already Equipped";
                slot.equipButton.interactable = false;
                slot.equipButton.onClick.RemoveAllListeners();
            }
            else
            {
                slot.equipButtonText.text = "Equip " + GetWeaponName(pendingType);
                slot.equipButton.interactable = true;
                slot.equipButton.onClick.RemoveAllListeners();
                slot.equipButton.onClick.AddListener(() =>
                {
                    hero.EquipWeaponInSlot(slotIndex, pendingType);
                    Close();
                });
            }
        }
        else
        {
            // Filled slot: show weapon + Replace + Boost.
            if (slot.iconImg != null)
            {
                slot.iconImg.sprite = equipped.hudIcon;
                slot.iconImg.color = equipped.hudIcon != null
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.18f);
                slot.iconImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -equipped.hudIconRotation);
            }
            if (slot.nameText != null)
            {
                slot.nameText.text = equipped.weaponName;
                slot.nameText.color = textColor;
            }

            slot.equipButtonGO.SetActive(false);
            slot.replaceButtonGO.SetActive(true);
            slot.boostButtonGO.SetActive(true);

            bool maxed = equipped.IsBoostMaxed(boostKind);
            // 3rd button (Hero Stat) only shows when the weapon is at its cap.
            slot.heroStatButtonGO.SetActive(maxed);

            // Re-anchor Replace/Boost so they cluster together visually
            // regardless of whether Hero Stat is shown below them.
            // 3-button mode: Replace=140, Boost=84, HeroStat=28.
            // 2-button mode: Replace=84, Boost=28 (closer to bottom).
            if (maxed)
            {
                SetButtonY(slot.replaceButtonGO, 140f);
                SetButtonY(slot.boostButtonGO,    84f);
                SetButtonY(slot.heroStatButtonGO, 28f);
            }
            else
            {
                SetButtonY(slot.replaceButtonGO, 84f);
                SetButtonY(slot.boostButtonGO,   28f);
            }

            // Replace button — swap this slot's weapon for the picked-up one.
            // Disabled if it would result in a no-op (same weapon already
            // here) or a duplicate (the other slot already has it).
            bool sameType = equipped.weaponType == pendingType;
            if (sameType)
            {
                slot.replaceButtonText.text = "Already " + equipped.weaponName;
                slot.replaceButton.interactable = false;
                slot.replaceButton.onClick.RemoveAllListeners();
            }
            else if (otherHasSameType)
            {
                slot.replaceButtonText.text = "Already Equipped";
                slot.replaceButton.interactable = false;
                slot.replaceButton.onClick.RemoveAllListeners();
            }
            else
            {
                slot.replaceButtonText.text = "Replace with " + GetWeaponName(pendingType);
                slot.replaceButton.interactable = true;
                slot.replaceButton.onClick.RemoveAllListeners();
                slot.replaceButton.onClick.AddListener(() =>
                {
                    hero.ReplaceWeaponSlot(slotIndex == 0, pendingType);
                    Close();
                });
            }

            // Boost button — apply the pickup-defined boost to THIS weapon.
            // When the weapon is past its cap the boost still applies, but at
            // postMaxBoostScale strength (smaller increments forever).
            // The label always spells out the exact stat change for that
            // weapon ("+10 Max Damage", "+1 Projectile", "+1 Explosion Radius",
            // "+X% Attack Speed", etc.).
            slot.boostButton.interactable = true;
            slot.boostButton.onClick.RemoveAllListeners();
            Weapon weaponRef = equipped;
            BoostKind kindRef = boostKind;

            string boostPrefix = maxed ? "Boost " + equipped.weaponName + " (post-max):  "
                                       : "Boost " + equipped.weaponName + ":  ";
            slot.boostButtonText.text = boostPrefix + equipped.DescribeBoost(boostKind);
            slot.boostButton.onClick.AddListener(() =>
            {
                hero.UpgradeWeaponPower(weaponRef, kindRef);
                Close();
            });

            // Hero Stat button — only relevant when maxed. Pre-rolls a random
            // hero stat (Max HP / Move Speed / Health Regen) and shows the
            // exact stat that will be granted, so the player can compare it
            // with the post-max weapon boost above.
            if (maxed)
            {
                Hero.HeroStatBoostMode preRolled = hero.RollHeroStatBoost();
                slot.heroStatButtonText.text = "Hero: " + hero.DescribeHeroStatBoost(preRolled);
                slot.heroStatButton.interactable = true;
                slot.heroStatButton.onClick.RemoveAllListeners();
                slot.heroStatButton.onClick.AddListener(() =>
                {
                    hero.ApplyHeroStatBoost(preRolled);
                    Close();
                });
            }
        }
    }

    private void Close()
    {
        // Drain queued pickups before fully closing. Time stays paused and
        // the music stays ducked across the chain — only the panel content
        // refreshes so the player gets to choose for every queued powerup.
        if (pendingQueue.Count > 0)
        {
            pendingType = pendingQueue.Dequeue();
            Refresh();
            return;
        }

        Hide();
        Time.timeScale = 1f;

        // Restore the music level (paired with BeginDuck in Show).
        if (MusicManager.Instance != null) MusicManager.Instance.EndDuck();
    }

    private void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // -------------------- UI construction --------------------

    private void BuildUI()
    {
        // Root canvas covering the screen.
        GameObject canvasGo = new GameObject("PowerUpChoiceCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above the GameHUD (100)

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Dim background that also catches clicks so the player can't
        // accidentally interact with the world while the panel is open.
        GameObject overlay = MakeUI("Overlay", canvasGo.transform);
        var overlayRt = (RectTransform)overlay.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        var overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = overlayColor;
        overlayImg.raycastTarget = true;

        panelRoot = overlay;

        // -- Title bar at the top: "Picked Up: <Name>" --
        GameObject titleGO = MakeUI("Title", overlay.transform);
        var titleRt = (RectTransform)titleGO.transform;
        titleRt.anchorMin = new Vector2(0.5f, 1f);
        titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -30f);
        titleRt.sizeDelta = new Vector2(900f, 120f); // tall enough for two lines
        titleText = titleGO.AddComponent<Text>();
        titleText.font = defaultFont;
        titleText.fontSize = 36;
        // Explicitly white so the "Picked Up:" header pops on the dim panel
        // regardless of how the serialized textColor has been tweaked.
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.UpperCenter;
        titleText.verticalOverflow = VerticalWrapMode.Overflow;
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        titleText.text = "Picked Up:\n";

        // -- Center: big picked-up icon + name (no buttons; informational) --
        // slotH grew from 460 → 540 to leave room for the 3rd button (Hero
        // Stat) that appears when the weapon is at its cap. The icon and name
        // padding below were nudged to keep things from overlapping.
        const int slotW = 320;
        const int slotH = 540;
        const int gap   = 60;

        // Compute three slot positions horizontally centered.
        int totalW = slotW * 3 + gap * 2;
        int leftX  = -totalW / 2;

        GameObject centerGO = BuildSlotPanel("Center", overlay.transform,
            new Vector2(0f, 0f), new Vector2(slotW, slotH));
        // Center "header" stays empty (it's just the picked-up display).
        BuildSlotHeader(centerGO.transform, "");
        centerIconImg = BuildSlotIcon(centerGO.transform, /*topPadding*/ 60);
        centerNameText = BuildSlotName(centerGO.transform, /*bottomPadding*/ 40, big: true);

        // Place center panel
        ((RectTransform)centerGO.transform).anchoredPosition = new Vector2(0f, 0f);

        // Build slot containers and place them on either side of the center.
        primarySlot = new SlotWidgets();
        secondarySlot = new SlotWidgets();

        primarySlot.root   = BuildSlotPanel("PrimarySlot",   overlay.transform,
            new Vector2(-(slotW + gap), 0f), new Vector2(slotW, slotH));
        secondarySlot.root = BuildSlotPanel("SecondarySlot", overlay.transform,
            new Vector2( (slotW + gap), 0f), new Vector2(slotW, slotH));

        // SlotWidgets is a struct, so pass by ref or the field assignments
        // inside BuildFullSlot vanish into a copy.
        BuildFullSlot(ref primarySlot,   primarySlot.root.transform);
        BuildFullSlot(ref secondarySlot, secondarySlot.root.transform);

        // Force the title bar to render last among the overlay's children so
        // it always sits in front of every slot panel regardless of which
        // siblings get added or rearranged later.
        if (titleText != null)
            titleText.transform.SetAsLastSibling();
    }

    private GameObject BuildSlotPanel(string name, Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = MakeUI(name, parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.color = panelColor;
        img.raycastTarget = true;
        AddOutline(go, panelOutlineColor);
        return go;
    }

    private void BuildFullSlot(ref SlotWidgets slot, Transform parent)
    {
        slot.headerText = BuildSlotHeader(parent, "PRIMARY");
        slot.iconImg = BuildSlotIcon(parent, /*topPadding*/ 60);
        slot.nameText = BuildSlotName(parent, /*bottomPadding*/ 224, big: false);

        // Buttons stack from the bottom of the slot.
        // 3-button mode: Replace (y=140), Boost (y=84), Hero Stat (y=28).
        // 2-button mode: Replace and Boost get re-anchored down to y=84/28.
        // Equip is the empty-slot fallback at y=56.

        // --- Equip button (single, when slot is empty) ---
        slot.equipButtonGO = BuildButton(parent, "EquipButton",
            new Vector2(0f, 56f), new Vector2(280f, 56f),
            out slot.equipButton, out slot.equipButtonText);
        slot.equipButtonText.text = "Equip";

        // --- Replace + Boost + Hero Stat (filled slot) ---
        slot.replaceButtonGO = BuildButton(parent, "ReplaceButton",
            new Vector2(0f, 140f), new Vector2(280f, 50f),
            out slot.replaceButton, out slot.replaceButtonText);
        slot.replaceButtonText.text = "Replace";

        slot.boostButtonGO = BuildButton(parent, "BoostButton",
            new Vector2(0f, 84f), new Vector2(280f, 50f),
            out slot.boostButton, out slot.boostButtonText);
        slot.boostButtonText.text = "Boost";

        slot.heroStatButtonGO = BuildButton(parent, "HeroStatButton",
            new Vector2(0f, 28f), new Vector2(280f, 50f),
            out slot.heroStatButton, out slot.heroStatButtonText);
        slot.heroStatButtonText.text = "Hero Stat";
    }

    private Text BuildSlotHeader(Transform parent, string headerText)
    {
        GameObject go = MakeUI("Header", parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -16f);
        rt.sizeDelta = new Vector2(0f, 32f);

        var t = go.AddComponent<Text>();
        t.font = defaultFont;
        t.fontSize = 22;
        t.color = subtleTextColor;
        t.alignment = TextAnchor.UpperCenter;
        t.text = headerText;
        t.raycastTarget = false;
        return t;
    }

    private Image BuildSlotIcon(Transform parent, int topPadding)
    {
        GameObject go = MakeUI("Icon", parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -topPadding);
        rt.sizeDelta = new Vector2(220f, 220f);

        var img = go.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    private Text BuildSlotName(Transform parent, int bottomPadding, bool big)
    {
        GameObject go = MakeUI("Name", parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottomPadding);
        rt.sizeDelta = new Vector2(0f, 36f);

        var t = go.AddComponent<Text>();
        t.font = defaultFont;
        t.fontSize = big ? 32 : 26;
        t.color = textColor;
        t.alignment = TextAnchor.LowerCenter;
        t.text = "";
        t.raycastTarget = false;
        return t;
    }

    private GameObject BuildButton(Transform parent, string name,
        Vector2 anchoredPosFromBottom, Vector2 size,
        out Button button, out Text label)
    {
        GameObject go = MakeUI(name, parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPosFromBottom;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.color = buttonColor;
        img.raycastTarget = true;

        button = go.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor = buttonPressedColor;
        colors.selectedColor = buttonHoverColor;
        colors.disabledColor = new Color(buttonColor.r, buttonColor.g, buttonColor.b, 0.45f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        // Label — wrap and auto-shrink so long boost descriptions (the bow's
        // multi-effect Crossbow-pickup label in particular) fit inside the
        // button instead of overflowing off the slot panel.
        GameObject labelGo = MakeUI("Label", go.transform);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 4f);
        labelRt.offsetMax = new Vector2(-8f, -4f);
        label = labelGo.AddComponent<Text>();
        label.font = defaultFont;
        label.fontSize = 20;
        label.color = textColor;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow   = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 10;
        label.resizeTextMaxSize = 24;
        label.raycastTarget = false;

        return go;
    }

    private GameObject MakeUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>Re-anchor a button's Y position relative to the bottom of its parent.</summary>
    private static void SetButtonY(GameObject buttonGO, float y)
    {
        if (buttonGO == null) return;
        var rt = (RectTransform)buttonGO.transform;
        Vector2 p = rt.anchoredPosition;
        p.y = y;
        rt.anchoredPosition = p;
    }

    private void AddOutline(GameObject go, Color color)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(1f, -1f);
    }

    private static string GetWeaponName(eWeaponType type)
    {
        switch (type)
        {
            case eWeaponType.sword:    return "Sword";
            case eWeaponType.shield:   return "Shield";
            case eWeaponType.bow:      return "Bow";
            case eWeaponType.dagger:   return "Dagger";
            case eWeaponType.crossbow: return "Crossbow";
            case eWeaponType.grenade:  return "Grenade";
            default:                   return "Unknown";
        }
    }
}
