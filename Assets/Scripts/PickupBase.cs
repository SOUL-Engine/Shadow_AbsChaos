using UnityEngine;

// ============================================================================
// PickupBase.cs                                                      (v0.9)
// GOES ON: nothing directly. HealthPickup and AmmoPickup build on it.
//          Put the file in Assets/Shadow/Scripts/.
//
// JOB: Everything every pickup shares: a trigger sphere, a gentle bob and spin, and
// "collect when Shadow touches it". A subclass only says WHAT happens (TryApply).
// Pickups spawn at runtime from enemy drops with no prefab: see Create<T>().
// ============================================================================
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public abstract class PickupBase : MonoBehaviour
{
    [Header("Look")]
    public float bobHeight = 0.15f;
    public float bobSpeed = 2.5f;
    public float spinSpeed = 120f;     // degrees per second

    Vector3 basePos;

    // Return true if the pickup was used (it is then destroyed). Return false to leave it
    // for later, e.g. a health pickup when you are already at full health.
    protected abstract bool TryApply(Collider collector);

    void Reset()
    {
        // Runs when the component is first added in the editor: set up the trigger sensibly.
        SphereCollider c = GetComponent<SphereCollider>();
        c.isTrigger = true;
        c.radius = 1.2f;               // generous pickup radius, bigger than the visible sphere
    }

    void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;         // a Rigidbody makes Unity reliably fire trigger events for the CharacterController
        rb.useGravity = false;
        basePos = transform.position;
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
    }

    void OnTriggerEnter(Collider other) => Collect(other);
    void OnTriggerStay(Collider other) => Collect(other); // so a pickup skipped at full health still works later

    void Collect(Collider other)
    {
        if (TryApply(other)) Destroy(gameObject);
    }

    // Builds a pickup out of a primitive sphere at runtime.
    //
    // NOTE (the v0.8 bug): the sphere primitive already carries a SphereCollider. We KEEP it and make it
    // the trigger. The old code destroyed it and then added the pickup, but Destroy only takes effect at
    // the end of the frame, so Unity still saw the old collider, did not add a new one, and then refused
    // to remove the one the pickup depended on.
    protected static T Create<T>(string objectName, Vector3 position, Color color) where T : PickupBase
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = objectName;
        g.transform.position = position;
        g.transform.localScale = Vector3.one * 0.45f;
        g.GetComponent<Renderer>().material.color = color;

        SphereCollider sc = g.GetComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 2.5f;              // local to the 0.45 scale: about 1.1 m in the world

        return g.AddComponent<T>();    // finds the SphereCollider above, and adds only the Rigidbody
    }
}
