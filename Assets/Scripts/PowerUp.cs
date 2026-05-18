using UnityEngine;
using TMPro;

/// <summary>
/// Collectible powerup dropped by enemies.
///
/// Expected prefab structure can be either:
///   PowerUp        <- root object with this script and TextMeshPro/TextMesh
///   └── PowerCube  <- cube visual child
///
/// or:
///   PowerUp        <- root object with this script
///   ├── Letter     <- TextMeshPro/TextMesh child
///   └── PowerCube  <- cube visual child
///
/// The script automatically finds the letter text and cube renderer.
/// It uses both trigger pickup and distance pickup so collection still works
/// if Unity layer collision settings are not perfect.
///
/// Letters:
///   S = Sword
///   H = Shield
///   B = Bow
///   D = Dagger
///   C = Crossbow
///   G = Grenade
/// </summary>
[RequireComponent(typeof(Collider))]
public class PowerUp : MonoBehaviour
{
    [Header("Powerup")]
    [SerializeField] private eWeaponType _type = eWeaponType.none;

    [Header("Pickup")]
    [Tooltip("Distance from the Hero where this powerup is collected.")]
    public float pickupRadius = 1.25f;

    [Header("Visual")]
    public Renderer cubeRenderer;
    public TextMeshPro tmpLetterText;
    public TextMesh letterText;

    [Header("Behavior")]
    public float lifeTime = 17f;
    public Vector3 rotateSpeed = new Vector3(0f, 120f, 0f);
    public float bobHeight = 0.25f;
    public float bobSpeed = 3f;

    [Header("Drop SFX")]
    [Tooltip("One-shot sound played at the powerup's position the moment it drops (Awake). Routed through SoundManager so it picks up the SFX volume slider + 3D rolloff. Leave null for silent drops.")]
    public AudioClip dropSound;
    [Tooltip("Per-clip volume multiplier for the drop SFX. Stacks on SoundManager.volume.")]
    [Range(0f, 1f)] public float dropSoundVolume = 1f;

    [Header("Colors")]
    public Color swordColor = new Color(1f, 0.35f, 0.25f, 1f);
    public Color shieldColor = new Color(0.25f, 0.65f, 1f, 1f);
    public Color bowColor = new Color(0.4f, 1f, 0.4f, 1f);
    public Color daggerColor = new Color(1f, 1f, 0.3f, 1f);
    public Color crossbowColor = new Color(0.65f, 0.45f, 0.25f, 1f);
    public Color grenadeColor = new Color(0.35f, 1f, 0.35f, 1f);
    public Color defaultColor = Color.white;

    private Vector3 startPosition;
    private float birthTime;
    private bool collected;
    private Collider pickupCollider;
    private Rigidbody rb;

    public eWeaponType type
    {
        get { return _type; }
        set { SetType(value); }
    }

    private void Awake()
    {
        FindParts();
        SetupPhysics();

        startPosition = transform.position;
        birthTime = Time.time;

        // Drop SFX — fires the moment the powerup spawns into the world.
        // Awake is the natural hook because powerups are instantiated at
        // their final position by the enemy that drops them; nothing
        // post-spawn relocates them.
        if (dropSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySfxAt(dropSound, transform.position, dropSoundVolume);
    }

    private void Start()
    {
        SetType(_type);
    }

    private void Update()
    {
        if (collected) return;

        if (cubeRenderer != null)
            cubeRenderer.transform.Rotate(rotateSpeed * Time.deltaTime, Space.World);

        if (bobHeight > 0f)
        {
            float y = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = startPosition + Vector3.up * y;
        }

        CheckDistancePickup();

        if (Time.time - birthTime >= lifeTime)
            Destroy(gameObject);
    }

    private void FindParts()
    {
        if (tmpLetterText == null)
            tmpLetterText = GetComponent<TextMeshPro>();

        if (tmpLetterText == null)
            tmpLetterText = GetComponentInChildren<TextMeshPro>(true);

        if (letterText == null)
            letterText = GetComponent<TextMesh>();

        if (letterText == null)
            letterText = GetComponentInChildren<TextMesh>(true);

        if (cubeRenderer == null)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer rend in renderers)
            {
                if (rend.GetComponent<TextMeshPro>() != null) continue;
                if (rend.GetComponent<TextMesh>() != null) continue;

                cubeRenderer = rend;
                break;
            }
        }
    }

    private void SetupPhysics()
    {
        pickupCollider = GetComponent<Collider>();
        pickupCollider.isTrigger = true;

        BoxCollider box = pickupCollider as BoxCollider;
        if (box != null)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 2f;
        }

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.isKinematic = true;
    }

    private void CheckDistancePickup()
    {
        Hero hero = Hero.Instance;
        if (hero == null || hero.IsDead) return;

        // Horizontal distance only — the powerup's height doesn't matter,
        // so the player can grab it without waiting for the bob to dip down.
        Vector3 toHero = hero.transform.position - transform.position;
        toHero.y = 0f;
        if (toHero.sqrMagnitude <= pickupRadius * pickupRadius) Collect(hero);
    }

    public void SetType(eWeaponType newType)
    {
        _type = newType;
        FindParts();

        if (cubeRenderer != null)
            cubeRenderer.material.color = GetColor(_type);

        string shownLetter = GetLetter(_type);

        if (tmpLetterText != null)
            tmpLetterText.text = shownLetter;

        if (letterText != null)
        {
            letterText.text = shownLetter;
            letterText.anchor = TextAnchor.MiddleCenter;
            letterText.alignment = TextAlignment.Center;
            letterText.color = Color.white;
        }
    }

    private string GetLetter(eWeaponType weaponType)
    {
        switch (weaponType)
        {
            case eWeaponType.sword: return "S";
            case eWeaponType.shield: return "H";
            case eWeaponType.bow: return "B";
            case eWeaponType.dagger: return "D";
            case eWeaponType.crossbow: return "C";
            case eWeaponType.grenade: return "G";
            default: return "?";
        }
    }

    private Color GetColor(eWeaponType weaponType)
    {
        switch (weaponType)
        {
            case eWeaponType.sword: return swordColor;
            case eWeaponType.shield: return shieldColor;
            case eWeaponType.bow: return bowColor;
            case eWeaponType.dagger: return daggerColor;
            case eWeaponType.crossbow: return crossbowColor;
            case eWeaponType.grenade: return grenadeColor;
            default: return defaultColor;
        }
    }

    public void Collect(Hero hero)
    {
        if (collected || hero == null) return;

        collected = true;
        hero.ApplyPowerUp(_type);
        Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        Hero hero = other.GetComponentInParent<Hero>();
        if (hero != null) Collect(hero);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
