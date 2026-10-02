using UnityEngine;

// A slow glowing bolt fired by GunDrone. Flies in a straight line, hurts Shadow on contact,
// and is stopped by ANY solid thing (walls, cover, even other enemies: bodies make cover).
public class DroneBolt : MonoBehaviour
{
    public float radius = 0.25f;
    public float life = 6f;
    public LayerMask hitMask = ~0;

    Vector3 dir;
    float speed, damage;

    public void Init(Vector3 direction, float boltSpeed, float boltDamage)
    {
        dir = direction.normalized;
        speed = boltSpeed;
        damage = boltDamage;
    }

    void Update()
    {
        float step = speed * Time.deltaTime;

        // Sweep a small sphere along this frame's path, so a fast bolt can't skip over a thin target.
        if (Physics.SphereCast(transform.position, radius, dir, out RaycastHit hit, step, hitMask,
                               QueryTriggerInteraction.Ignore))
        {
            ShadowHealth h = hit.collider.GetComponentInParent<ShadowHealth>();
            if (h != null) h.TakeDamage(damage, hit.point, DamageKind.Enemy);
            Destroy(gameObject);
            return;
        }

        transform.position += dir * step;

        life -= Time.deltaTime;
        if (life <= 0f) Destroy(gameObject);
    }
}