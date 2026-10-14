using UnityEngine;

// ============================================================================
// ShadowTestWeapon.cs                                                (v0.3)
// GOES ON: Player (root). It finds ShadowMotor and ShadowInput on its own.
//
// JOB: A throwaway test weapon to prove the aim pipeline end to end.
//   LMB (hold) = rapid hitscan pistol
//   RMB        = shotgun blast (8 pellets, short range, cooldown)
//
// HOW AIMING WORKS (v0.7): the CROSSHAIR RAY decides every hit. It starts at the
// camera (motor.CrosshairCast), so "reticle on the enemy" always means a hit,
// including point-blank right after a homing attack, in the air or on the ground.
// (Before v0.7 the ray started at the gun. Right after a homing hit the gun is
// INSIDE the enemy's collider, and a ray that starts inside a collider cannot hit it.)
// The tracer is drawn from the muzzle to the hit point. A cover check from the
// muzzle still stops you shooting through a wall edge: if a DIFFERENT solid object
// sits between the arm and the target, the shot hits that instead.
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

    [Header("Ammo caps (ammo scarcity is a pillar)")]
    public int maxPrimaryAmmo = 150;
    public int maxAltAmmo = 12;

    [Header("Aim assist (per weapon: tune each one separately)")]
    public AimAssistProfile primaryAssist = new AimAssistProfile();   // pistol: forgiving
    // Shotgun: no hit forgiveness (the spread already forgives), a little friction.
    public AimAssistProfile altAssist = new AimAssistProfile { coneDegrees = 0f, frictionAmount = 0.25f };

    // Which profile the camera should use for mouse friction right now.
    public AimAssistProfile CurrentAssist => input.AltFireHeld ? altAssist : primaryAssist;

    [Header("Testing")]
    public bool infiniteAmmo = true;   // UNTICK once enemies exist: ammo scarcity is a pillar
    public float muzzleDistance = 0.45f; // how far in front of the arm centre bullets start
    public float tracerWidth = 0.03f;
    public float tracerLife = 0.06f;

    public int PrimaryAmmo => primaryAmmo;
    public int AltAmmo => altAmmo;

    // Adds ammo up to the caps. Returns false if NOTHING could be added (both already full),
    // so an ammo pickup can wait for you instead of being wasted.
    public bool AddAmmo(int bullets, int shells)
    {
        int p0 = primaryAmmo, a0 = altAmmo;
        primaryAmmo = Mathf.Min(maxPrimaryAmmo, primaryAmmo + bullets);
        altAmmo = Mathf.Min(maxAltAmmo, altAmmo + shells);
        return primaryAmmo != p0 || altAmmo != a0;
    }

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
        if (!motor.CanShoot) return; // no shooting in spin dash / ball dash / mantle

        if (input.FireHeld && Time.time >= nextPrimaryTime && (infiniteAmmo || primaryAmmo > 0))
        {
            nextPrimaryTime = Time.time + 1f / fireRate;
            if (!infiniteAmmo) primaryAmmo--;
            FireRay(BaseDirection(), range, damage, new Color(1f, 0.9f, 0.3f), primaryAssist);
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
                FireRay(dir, altRange, pelletDamage, new Color(1f, 0.4f, 0.2f), altAssist);
            }
        }
    }

    Vector3 Muzzle() => motor.aimArm.position + motor.aimArm.forward * muzzleDistance;

    // The aim direction is simply where the camera (and so the crosshair) points.
    Vector3 BaseDirection() => motor.rig.cam.transform.forward;

    void FireRay(Vector3 dir, float maxRange, float dmg, Color tracerColor, AimAssistProfile assist)
    {
        Vector3 muzzle = Muzzle();
        Vector3 end = muzzle + dir * maxRange;
        bool landedOnTarget = false;

        // 1. What is under the crosshair? (camera ray: ignores Shadow, ignores things behind him)
        if (motor.CrosshairCast(dir, maxRange, out RaycastHit hit))
        {
            // 2. Cover check from the arm (unchanged): a DIFFERENT solid object between the muzzle
            //    and the target takes the shot instead.
            Vector3 toHit = hit.point - muzzle;
            float dist = toHit.magnitude;
            if (dist > 0.1f
                && motor.WorldCast(muzzle, toHit / dist, dist - 0.05f, out RaycastHit cover)
                && cover.collider != hit.collider
                && !SameTarget(cover.collider, hit.collider))
            {
                hit = cover;
            }

            end = hit.point;
            // Deal() returns true only if what we hit can be hurt. A wall returns false.
            landedOnTarget = Damage.Deal(hit.collider, dmg, hit.point, DamageKind.Gun);
        }

        // 3. AIM ASSIST: the shot hit a wall or nothing. If an enemy was inside the forgiveness cone and
        //    nothing solid is in the way, it becomes a BODY hit on that enemy instead.
        if (!landedOnTarget && TryAssistedHit(dir, maxRange, dmg, assist, muzzle, out Vector3 assistPoint))
            end = assistPoint;

        SpawnTracer(muzzle, end, tracerColor);
    }

    // Converts a near-miss into a hit. Returns true (and the point to draw the tracer to) if it did.
    bool TryAssistedHit(Vector3 dir, float maxRange, float dmg, AimAssistProfile a, Vector3 muzzle, out Vector3 point)
    {
        point = default;
        float s = AimAssist.Strength;                       // the player's 0..1 setting
        if (a == null || !a.enabled || s <= 0f || a.coneDegrees <= 0f) return false;

        Transform cam = motor.rig.cam.transform;
        // Ignore anything behind Shadow, as seen from the camera (same rule as the crosshair ray).
        float minDepth = Vector3.Dot(transform.position + Vector3.up - cam.position, cam.forward) - 0.75f;

        if (!AimAssist.FindBest(cam.position, dir, maxRange, a.coneDegrees * s, a.coneMaxRadius * s,
                                motor.aimMask, transform, minDepth, out AimAssist.Result r))
            return false;

        // Solid cover between the gun and the enemy? That is cover, not a miss to forgive.
        Vector3 toP = r.point - muzzle;
        float dist = toP.magnitude;
        if (dist > 0.1f
            && motor.WorldCast(muzzle, toP / dist, dist - 0.05f, out RaycastHit cover)
            && !SameTarget(cover.collider, r.collider))
            return false;

        // Straight to the enemy, bypassing Hurtbox zone multipliers: always 1x BODY damage, never a headshot.
        r.target.TakeDamage(dmg, r.point, DamageKind.Gun);
        point = r.point;
        return true;
    }

    // Two colliders belong to the same enemy if they share the same IDamageable (e.g. head + body).
    static bool SameTarget(Collider a, Collider b)
    {
        IDamageable da = a.GetComponentInParent<IDamageable>();
        return da != null && da == b.GetComponentInParent<IDamageable>();
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