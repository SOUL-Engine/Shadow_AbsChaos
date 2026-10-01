using UnityEngine;

// ============================================================================
// HealthPickup.cs                                                    (v0.8)
// GOES ON: a Sphere with this script. It adds its own trigger collider and a
// kinematic Rigidbody (needed so Unity reliably fires trigger events for Shadow's
// CharacterController). Enemies also create these from code with HealthPickup.Spawn().
//
// JOB: Walk into it to heal. Since Shadow does NOT regenerate, this is the only way
// back up. Like Doom, a pickup is NOT wasted at full health: it waits for you.
// ============================================================================
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class HealthPickup : MonoBehaviour
{
    public float amount = 25f;
    public bool onlyWhenHurt = true;   // at full health the pickup stays put until you need it
    public float bobHeight = 0.15f;
    public float bobSpeed = 2.5f;
    public float spinSpeed = 120f;     // degrees per second

    Vector3 basePos;

    void Reset()
    {
        // Runs when the component is first added in the editor: set up the trigger sensibly.
        SphereCollider c = GetComponent<SphereCollider>();
        c.isTrigger = true;
        c.radius = 1.2f;               // generous pickup radius, bigger than the visible sphere
    }

    void Awake()
    {
        SphereCollider c = GetComponent<SphereCollider>();
        c.isTrigger = true;
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        basePos = transform.position;
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
    }

    void OnTriggerEnter(Collider other) => TryCollect(other);
    void OnTriggerStay(Collider other) => TryCollect(other); // so a pickup you skipped at full health still works later

    void TryCollect(Collider other)
    {
        ShadowHealth health = other.GetComponentInParent<ShadowHealth>();
        if (health == null || health.IsDead) return;
        if (onlyWhenHurt && health.IsFull) return;

        health.Heal(amount);
        Destroy(gameObject);
    }

    // Creates a green pickup at runtime (used by enemy drops). No prefab needed.
    public static HealthPickup Spawn(Vector3 position, float healAmount)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "HealthPickup";
        Destroy(g.GetComponent<Collider>());               // the primitive's solid collider is not wanted
        g.transform.position = position;
        g.transform.localScale = Vector3.one * 0.45f;
        g.GetComponent<Renderer>().material.color = new Color(0.2f, 1f, 0.35f); // green = health

        HealthPickup p = g.AddComponent<HealthPickup>();   // also adds SphereCollider + Rigidbody
        p.amount = healAmount;
        p.GetComponent<SphereCollider>().isTrigger = true;
        p.GetComponent<SphereCollider>().radius = 2.5f;    // local to the 0.45 scale: ~1.1 m in the world
        return p;
    }
}
