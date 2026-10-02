using UnityEngine;

// ============================================================================
// IDamageable.cs                                                     (v0.9)
// GOES ON: nothing. It is an interface + an enum. Put the file in Assets/Shadow/Scripts/.
//
// JOB: The ONE contract for "something that can be hurt". Shadow's gun, spin dash,
// ball dash and homing attack find targets by looking for this interface, and enemy
// shots hurt Shadow through it too (ShadowHealth implements it).
//
// THE DAMAGE KIND says HOW the damage happened. An enemy remembers the kind of the
// hit that killed it, and its EnemyDrops decides what it drops from that:
//   Gun       -> ammo      (pistol and shotgun)
//   SpinDash  -> health    (wading in is how you heal: the Courageous pillar)
//   Homing    -> some ammo (a homing CHAIN also refills stamina, see the motor)
//   Enemy     -> damage dealt TO Shadow by an enemy
//   Other     -> hazards, scripted damage: no drops
//
// HOW TO USE: on any enemy/dummy script, add `, IDamageable` after MonoBehaviour
// and write a TakeDamage method. The collider can be on the same object or on a
// child: the search walks UP the hierarchy from whatever collider it hits.
// ============================================================================
public enum DamageKind { Gun, SpinDash, Homing, Enemy, Other }

public interface IDamageable
{
    // amount = damage dealt. point = where it landed (for hit effects). kind = how it happened.
    void TakeDamage(float amount, Vector3 point, DamageKind kind);
}