using UnityEngine;

// ============================================================================
// ShadowTestWeapon.cs                                                (v0.3)
// GOES ON: Player (root). It finds ShadowMotor and ShadowInput on its own.
//
// JOB: A throwaway test weapon to prove the aim pipeline end to end.
//   LMB (hold) = rapid hitscan pistol
//   RMB        = shotgun blast (8 pellets, short range, cooldown)
//
// HOW AIMING WORKS: the crosshair ray (motor.AimPoint) decides WHERE we aim.
// Bullets then leave the arm's muzzle toward that point. A second raycast from
// the muzzle catches anything standing between the arm and the crosshair target
// (corners, cover), so you cannot shoot through a wall edge.
//
// Runs late (DefaultExecutionOrder 100) so the camera and AimPoint are final this frame.
// ============================================================================
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(ShadowMotor))]
[RequireComponent(typeof(ShadowInput))]
public class ShadowTestWeapon : MonoBehaviour
{
    [Header("Primary (LMB): hitscan pistol")]
    public float fireRate = 8f;       // shots per second
    public float damage = 10f;
    public float range = 200f;
    public int primaryAmmo = 120;

    [Header("Alt (RMB): shotgun blast")]
    public int pellets = 8;
    public float spreadDegrees = 5f;
    public float pelletDamage = 8f;
    public float altRange = 30f;
    public float altCooldown = 0.8f;
    public int altAmmo = 8;

    [Header("Testing")]
    public bool infiniteAmmo = true;   // UNTICK once enemies exist: ammo scarcity is a pillar
    public float muzzleDistance = 0.45f; // how far in front of the arm centre bullets start
    public float tracerWidth = 0.03f;
    public float tracerLife = 0.06f;

    public int PrimaryAmmo => primaryAmmo;
    public int AltAmmo => altAmmo;

    ShadowMotor motor;
    ShadowInput input;
    float nextPrimaryTime, nextAltTime;

    void Awake()
    {
        motor = GetComponent<ShadowMotor>();
        input = GetComponent<ShadowInput>();
    }

    void LateUpdate()
    {
        if (input.FireHeld && Time.time >= nextPrimaryTime && (infiniteAmmo || primaryAmmo > 0))
        {
            nextPrimaryTime = Time.time + 1f / fireRate;
            if (!infiniteAmmo) primaryAmmo--;
            FireRay(BaseDirection(), range, damage, new Color(1f, 0.9f, 0.3f));
        }

        if (input.AltFireHeld && Time.time >= nextAltTime && (infiniteAmmo || altAmmo > 0))
        {
            nextAltTime = Time.time + altCooldown;
            if (!infiniteAmmo) altAmmo--;

            Quaternion look = Quaternion.LookRotation(BaseDirection());
            for (int i = 0; i < pellets; i++)
            {
                // Random point inside a cone around the aim direction.
                Vector2 r = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
                Vector3 dir = (look * new Vector3(r.x, r.y, 1f)).normalized;
                FireRay(dir, altRange, pelletDamage, new Color(1f, 0.4f, 0.2f));
            }
        }
    }

    Vector3 Muzzle() => motor.aimArm.position + motor.aimArm.forward * muzzleDistance;

    // Direction from the muzzle to the crosshair target (falls back to camera forward if too close).
    Vector3 BaseDirection()
    {
        Vector3 toAim = motor.AimPoint - Muzzle();
        return toAim.sqrMagnitude > 0.25f ? toAim.normalized : motor.rig.cam.transform.forward;
    }

    void FireRay(Vector3 dir, float maxRange, float dmg, Color tracerColor)
    {
        Vector3 muzzle = Muzzle();
        Vector3 end = muzzle + dir * maxRange;

        if (Physics.Raycast(muzzle, dir, out RaycastHit hit, maxRange, motor.aimMask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            ShadowTestTarget t = hit.collider.GetComponentInParent<ShadowTestTarget>();
            if (t != null) t.Hit(dmg, hit.point);
        }
        SpawnTracer(muzzle, end, tracerColor);
    }

    // Prototype tracer: a thin stretched cube that deletes itself. Fine for testing; pool it later.
    void SpawnTracer(Vector3 from, Vector3 to, Color color)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.05f) return;

        GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(t.GetComponent<Collider>()); // visual only
        t.transform.position = from + d * 0.5f;
        t.transform.rotation = Quaternion.LookRotation(d);
        t.transform.localScale = new Vector3(tracerWidth, tracerWidth, len);
        t.GetComponent<Renderer>().material.color = color;
        Destroy(t, tracerLife);
    }
}
