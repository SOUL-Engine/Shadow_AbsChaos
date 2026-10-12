using UnityEngine;

// A static helper: the ONE place that turns "this collider was hit" into damage on whatever
// owns it, so zones and multipliers work the same for every weapon.
public static class Damage
{
    public static bool Deal(Collider hit, float amount, Vector3 point, DamageKind kind)
    {
        // A Hurtbox applies its zone multiplier, then reaches the owner.
        Hurtbox hurtbox = hit.GetComponent<Hurtbox>();
        if (hurtbox != null) return hurtbox.Receive(amount, point, kind);

        // Otherwise (a plain body collider, a dummy) go straight to the nearest IDamageable above it.
        IDamageable target = hit.GetComponentInParent<IDamageable>();
        if (target == null) return false;
        target.TakeDamage(amount, point, kind);
        return true;
    }
}