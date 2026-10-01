using UnityEngine;

// ============================================================================
// IDamageable.cs                                                     (v0.5)
// GOES ON: nothing. It is an interface. Put the file in Assets/Shadow/Scripts/.
//
// JOB: The ONE contract for "something that can be hurt". Shadow's spin dash,
// ball dash and homing attack find targets by looking for this interface, and
// the health system and real enemies (next step) will implement it too.
//
// HOW TO USE: on any enemy/dummy script, add `, IDamageable` after MonoBehaviour
// and write a TakeDamage method. The collider can be on the same object or on a
// child: the search walks UP the hierarchy from whatever collider it hits.
// ============================================================================
public interface IDamageable
{
    // amount = damage dealt. point = where it landed (for hit effects).
    void TakeDamage(float amount, Vector3 point);
}
