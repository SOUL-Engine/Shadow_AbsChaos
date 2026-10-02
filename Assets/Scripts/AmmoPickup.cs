using UnityEngine;

// ============================================================================
// AmmoPickup.cs                                                      (v0.9)
// GOES ON: a Sphere with this script (it adds its own trigger collider and a kinematic
// Rigidbody). Enemies also create these from code with AmmoPickup.Spawn().
//
// JOB: Walk into it to refill the gun. Ammo scarcity is a pillar, so the amounts are small
// and the weapon has a cap. Like health, a pickup waits for you if you are already full.
// ============================================================================
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class AmmoPickup : PickupBase
{
    public int bullets = 20;   // pistol ammo
    public int shells = 1;     // shotgun ammo

    protected override bool TryApply(Collider collector)
    {
        ShadowTestWeapon weapon = collector.GetComponentInParent<ShadowTestWeapon>();
        if (weapon == null) return false;
        return weapon.AddAmmo(bullets, shells); // false (and the pickup stays) when both are full
    }

    // Creates a yellow pickup at runtime (used by enemy drops). No prefab needed.
    public static AmmoPickup Spawn(Vector3 position, int bulletAmount, int shellAmount)
    {
        AmmoPickup p = Create<AmmoPickup>("AmmoPickup", position, new Color(1f, 0.85f, 0.15f)); // yellow = ammo
        p.bullets = bulletAmount;
        p.shells = shellAmount;
        return p;
    }
}
