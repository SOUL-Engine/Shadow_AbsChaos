using UnityEngine;

// ============================================================================
// ShadowTestTarget.cs                                                (v0.3)
// GOES ON: any Cube / Capsule with a Collider, on the DEFAULT layer (NOT Player).
//
// JOB: A shootable dummy. Flashes red when hit, disappears at 0 health.
// Throwaway: real enemies replace this later.
// ============================================================================
public class ShadowTestTarget : MonoBehaviour
{
    public float health = 100f;
    public float flashTime = 0.08f;

    Renderer rend;
    Color baseColor;
    float flashUntil;

    void Awake()
    {
        rend = GetComponentInChildren<Renderer>();
        baseColor = rend.material.color;
    }

    // Called by ShadowTestWeapon.
    public void Hit(float damage, Vector3 point)
    {
        health -= damage;
        flashUntil = Time.time + flashTime;
        if (health <= 0f) Destroy(gameObject);
    }

    void Update()
    {
        rend.material.color = Time.time < flashUntil ? Color.red : baseColor;
    }
}
