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

    // Builds a pickup at runtime with NO prefab.
    //
    // The root object holds only the trigger collider, the Rigidbody and the pickup script, and
    // nothing ever removes them. The visible sphere is a separate CHILD, so removing ITS collider
    // is safe (nothing depends on it).
    protected static T Create<T>(string objectName, Vector3 position, Color color) where T : PickupBase
    {
        GameObject root = new GameObject(objectName);
        root.transform.position = position;

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.1f;                 // world metres (the root is not scaled)

        T pickup = root.AddComponent<T>();     // finds the collider above, adds only the Rigidbody

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(visual.GetComponent<Collider>());              // safe: a child's collider
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * 0.45f;
        visual.GetComponent<Renderer>().material.color = color;

        return pickup;
    }
}
