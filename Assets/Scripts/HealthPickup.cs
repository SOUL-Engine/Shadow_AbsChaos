using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class HealthPickup : PickupBase
{
    public float amount = 25f;
    public bool onlyWhenHurt = true;   // at full health the pickup waits for you

    protected override bool TryApply(Collider collector)
    {
        ShadowHealth health = collector.GetComponentInParent<ShadowHealth>();
        if (health == null || health.IsDead) return false;
        if (onlyWhenHurt && health.IsFull) return false;

        health.Heal(amount);
        return true;
    }

    public static HealthPickup Spawn(Vector3 position, float healAmount)
    {
        HealthPickup p = Create<HealthPickup>("HealthPickup", position, new Color(0.2f, 1f, 0.35f));
        p.amount = healAmount;
        return p;
    }
}