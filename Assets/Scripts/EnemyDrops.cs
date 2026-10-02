using UnityEngine;

// ============================================================================
// EnemyDrops.cs                                                      (v0.9)
// GOES ON: any enemy. Enemy scripts add it automatically ([RequireComponent]), so you
// normally just tune the numbers in the Inspector, per enemy type.
//
// JOB: Decides what an enemy drops, based on HOW the killing blow was dealt. This is the
// Doom Eternal resource triangle, built for Shadow:
//
//        GUN kills         -> AMMO                 (keep shooting)
//        SPIN DASH kills   -> HEALTH               (wade in to heal: Courageous)
//        HOMING kills      -> some AMMO            (and a homing CHAIN refills stamina)
//
// Everything else (hazards, scripted damage) drops nothing.
// ============================================================================
public class EnemyDrops : MonoBehaviour
{
    [Header("Killed with the GUN (pistol / shotgun) -> AMMO")]
    [Range(0f, 1f)] public float gunKillAmmoChance = 0.6f;
    public int gunKillBullets = 20;
    public int gunKillShells = 1;

    [Header("Killed with the SPIN DASH -> HEALTH")]
    [Range(0f, 1f)] public float spinKillHealthChance = 1f;   // guaranteed: it is the way to heal
    public float spinKillHealth = 15f;

    [Header("Killed with the HOMING ATTACK -> some AMMO")]
    [Range(0f, 1f)] public float homingKillAmmoChance = 0.8f;
    public int homingKillBullets = 10;
    public int homingKillShells = 0;

    // Called by the enemy when it dies. `kind` is the kind of the hit that killed it.
    public void Drop(DamageKind kind, Vector3 position)
    {
        switch (kind)
        {
            case DamageKind.Gun:
                if (Random.value <= gunKillAmmoChance)
                    AmmoPickup.Spawn(position, gunKillBullets, gunKillShells);
                break;

            case DamageKind.SpinDash:
                if (Random.value <= spinKillHealthChance)
                    HealthPickup.Spawn(position, spinKillHealth);
                break;

            case DamageKind.Homing:
                if (Random.value <= homingKillAmmoChance)
                    AmmoPickup.Spawn(position, homingKillBullets, homingKillShells);
                break;
        }
    }
}
