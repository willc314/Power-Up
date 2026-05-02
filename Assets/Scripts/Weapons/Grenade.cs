using UnityEngine;

/// <summary>
/// In-flight grenade. Travels in a parabolic arc to a target point chosen by
/// GrenadeWeapon, lands, and triggers an Explosion at its position.
/// Tumbles for visual flair while in the air.
/// </summary>
public class Grenade : MonoBehaviour
{
    [Header("Detonation")]
    [Tooltip("Prefab spawned at the landing point. Should have an Explosion component.")]
    public Explosion explosionPrefab;

    [Header("Visual")]
    [Tooltip("Tumble speed while in flight, deg/sec.")]
    public float tumbleSpeed = 360f;
    [Tooltip("Random tumble axis, used as-is (will be normalized).")]
    public Vector3 tumbleAxis = new Vector3(1f, 0.5f, 0.3f);

    private Vector3 startPos;
    private Vector3 endPos;
    private float arcHeight;
    private float duration;
    private float timer;

    public void Launch(Vector3 startPos, Vector3 endPos, float arcHeight, float flightTime)
    {
        this.startPos = startPos;
        this.endPos = endPos;
        this.arcHeight = arcHeight;
        this.duration = Mathf.Max(0.05f, flightTime);
        timer = 0f;
        transform.position = startPos;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        // Parabolic interpolation: linear xz + quadratic y.
        Vector3 pos = Vector3.Lerp(startPos, endPos, t);
        pos.y += 4f * arcHeight * t * (1f - t); // peaks at t=0.5
        transform.position = pos;

        if (tumbleSpeed != 0f && tumbleAxis.sqrMagnitude > 0.0001f)
            transform.Rotate(tumbleAxis.normalized, tumbleSpeed * Time.deltaTime, Space.World);

        if (t >= 1f) Detonate();
    }

    private void Detonate()
    {
        if (explosionPrefab != null)
        {
            Explosion ex = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            ex.Detonate();
        }
        Destroy(gameObject);
    }
}
