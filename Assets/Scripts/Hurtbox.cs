using UnityEngine;

// What part of the body this hurtbox is. Add more zones here as characters need them.
public enum HitZone { Body, Head, Limb, WeakPoint }

// GOES ON: an EMPTY child object of a character, next to a collider (sphere / capsule / box).
// Put the empty under the matching BONE of the mesh so it animates with the character.
// The Hurtbox and its collider must be on the SAME GameObject.
public class Hurtbox : MonoBehaviour
{
    [Header("What this part of the body does")]
    public HitZone zone = HitZone.Body;
    public float damageMultiplier = 1f;      // Head ~2, Body 1, Limb ~0.6, WeakPoint ~3

    [Header("Owner (found automatically in a parent)")]
    public MonoBehaviour ownerOverride;      // only set this if the owner is NOT a parent of this object

    IDamageable owner;

    // Anything can listen to this (hit markers, hit sounds, damage numbers) without the weapon knowing.
    public static event System.Action<Hurtbox, float, DamageKind> AnyHit;

    void Awake()
    {
        owner = ownerOverride as IDamageable;
        if (owner == null) owner = GetComponentInParent<IDamageable>();
        if (owner == null)
            Debug.LogWarning($"Hurtbox '{name}' has no IDamageable owner in its parents, so it can't pass damage on.", this);
    }

    // Called by Damage.Deal. Returns true if the damage reached an owner.
    public bool Receive(float amount, Vector3 point, DamageKind kind)
    {
        if (owner == null) return false;
        float final = amount * damageMultiplier;
        owner.TakeDamage(final, point, kind);
        AnyHit?.Invoke(this, final, kind);
        return true;
    }

    // Editor-time check: a Hurtbox with no collider can never be hit, so say so loudly.
    void OnValidate()
    {
        if (GetComponent<Collider>() == null)
            Debug.LogWarning($"Hurtbox '{name}' needs a Collider on the same GameObject.", this);
    }

}
