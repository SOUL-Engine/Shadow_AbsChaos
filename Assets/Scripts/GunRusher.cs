using UnityEngine;

// ============================================================================
// GunRusher.cs                                                       (v0.10)
// GOES ON: a Capsule (the capsule primitive already has the CapsuleCollider this needs).
//   The script adds a Rigidbody and an EnemyDrops for you.
//   Make him look DIFFERENT from the Grunt so you read "this one charges" in one second:
//   a squat, wide ORANGE capsule works well, e.g. Scale (1.3, 0.7, 1.3). He stays under
//   Shadow's jump height, so jumping over him is a valid answer.
//   Put him on the DEFAULT layer, NOT Player.
//
// JOB: The sandbox's melee enemy. He closes the distance and CHARGES in a straight line.
//
// WHY THIS DESIGN (boomer shooter checklist):
//   * Readable: a red LANE on the floor shows exactly where he will run. It tracks you,
//     then FREEZES and brightens just before he goes. He also pulses white while winding up.
//   * Fair: the lane is locked for `lockTime` before the charge, so a sidestep, a jump,
//     or a Chaos warp always beats it. Standing in the lane is your choice.
//   * Punishable: after a charge he is OUT OF BREATH for `recoverTime` and takes extra
//     damage. A charge into a wall leaves him stunned for the full time.
//   * Answers the Grunt's weakness: the Grunt keeps his distance, the Rusher forces a fight.
//     The wind-up is long enough for a spin dash to reach him from ~14 m before he runs,
//     which is exactly the move that heals you when it kills him.
//   * Tests the kit: sidestep / warp / jump-over, spin dash into the wind-up, homing,
//     and baiting charges into walls.
//
// Takes damage through IDamageable, so every weapon and move you have works on him.
// ============================================================================
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(EnemyDrops))]   // what he drops is tuned on the EnemyDrops component
public class GunRusher : MonoBehaviour, IDamageable
{
    [Header("Health (drops are set on the EnemyDrops component)")]
    public float maxHealth = 60f;
    public float recoverDamageMultiplier = 1.5f; // takes extra damage while catching his breath

    [Header("Approach")]
    public float aggroRange = 35f;
    public float approachSpeed = 7f;
    public float turnSpeed = 540f;               // degrees per second

    [Header("Charge (telegraphed)")]
    public float chargeTriggerRange = 16f;       // starts winding up when you are this close (and visible)
    public float telegraphTime = 0.75f;          // the lane TRACKS you for this long...
    public float lockedTurnRate = 45f;           // the frozen lane can still swing toward you this many degrees/second (0 = truly fixed)
    public float lockTime = 0.25f;               // ...then freezes (bright) for this long, then he goes
    public float chargeSpeed = 24f;
    public float chargeDistance = 24f;           // how far a charge runs if nothing stops it
    public float meleeDamage = 22f;
    public float hitRadiusPadding = 0.4f;        // how far beyond his body counts as a hit
    public float recoverTime = 1.1f;             // out of breath after a missed charge or a wall crash
    public float recoverAfterHitTime = 0.6f;     // shorter if the charge actually connected
    public float chargeCooldown = 0.5f;          // extra pause before he can wind up again
    public LayerMask hitMask = ~0;

    enum State { Idle, Approach, Telegraph, Locked, Charge, Recover }

    State state = State.Idle;
    Rigidbody rb;
    CapsuleCollider col;
    EnemyDrops drops;
    ShadowHealth player;
    LineRenderer lane;
    Renderer[] rends;
    Color[] baseColors;

    float health;
    bool dead;
    Vector3 desiredVel;
    Vector3 lockedDir;
    Vector3 lastPos;
    float timer, chargeTravelled, nextChargeTime, flashUntil;
    readonly Collider[] hitBuf = new Collider[16];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<CapsuleCollider>();
        drops = GetComponent<EnemyDrops>();
        rb.freezeRotation = true;                              // he turns by script, physics must not tip him over
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        health = maxHealth;
        rends = GetComponentsInChildren<Renderer>();
        baseColors = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++) baseColors[i] = rends[i].material.color;

        BuildLane();
        nextChargeTime = Time.time + Random.Range(0.3f, 1.2f); // a group shouldn't all wind up on the same frame
    }

    void Start()
    {
        player = FindAnyObjectByType<ShadowHealth>();
    }

    void OnDestroy()
    {
        if (lane != null) Destroy(lane.gameObject);            // the lane is not parented to us
    }

    // ---------------------------------------------------------------- AI
    void Update()
    {
        UpdateVisuals();

        if (dead || player == null || player.IsDead)
        {
            desiredVel = Vector3.zero;
            lane.enabled = false;
            return;
        }

        Vector3 center = col.bounds.center;
        Vector3 target = player.transform.position + Vector3.up;   // the player's pivot is at his feet
        Vector3 to = target - center;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        float fd = flat.magnitude;
        bool sees = fd <= aggroRange && HasLineOfSight(center, to / Mathf.Max(to.magnitude, 0.01f), to.magnitude);

        switch (state)
        {
            case State.Idle:
                desiredVel = Vector3.zero;
                if (sees) state = State.Approach;
                break;

            case State.Approach:
                Face(flat);
                desiredVel = fd > 1.5f ? flat / fd * approachSpeed : Vector3.zero;
                if (sees && fd <= chargeTriggerRange && Time.time >= nextChargeTime) BeginTelegraph();
                break;

            case State.Telegraph:
                desiredVel = Vector3.zero;                      // stops dead to wind up: readable
                Face(flat);
                if (fd > 0.01f) lockedDir = flat / fd;          // the lane tracks you
                DrawLane(false);
                timer -= Time.deltaTime;
                if (timer <= 0f) { state = State.Locked; timer = lockTime; }
                break;

            case State.Locked:
                desiredVel = Vector3.zero;
                // A little "moving room": the lane may still swing toward you at lockedTurnRate degrees/second.
                if (fd > 0.01f)
                    lockedDir = Vector3.RotateTowards(lockedDir, flat / fd, lockedTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f);
                Face(lockedDir);
                DrawLane(true);                                  // frozen and bright: MOVE NOW
                timer -= Time.deltaTime;
                if (timer <= 0f) BeginCharge();
                break;

            case State.Charge:
                ChargeUpdate();
                break;

            case State.Recover:
                desiredVel = Vector3.zero;                       // out of breath: punish him
                timer -= Time.deltaTime;
                if (timer <= 0f) { state = State.Approach; nextChargeTime = Time.time + chargeCooldown; }
                break;
        }
    }

    void FixedUpdate()
    {
        // Horizontal movement from the AI, vertical left to gravity.
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(desiredVel.x, v.y, desiredVel.z);
    }

    void Face(Vector3 flat)
    {
        if (flat.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat),
                                                      turnSpeed * Time.deltaTime);
    }

    bool HasLineOfSight(Vector3 from, Vector3 dir, float dist)
    {
        // The first thing in the way must be the player (or nothing). Starting inside our own
        // collider means we never hit ourselves.
        if (!Physics.Raycast(from, dir, out RaycastHit hit, dist, hitMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.GetComponentInParent<ShadowHealth>() != null;
    }

    // ---------------------------------------------------------------- the charge
    void BeginTelegraph()
    {
        state = State.Telegraph;
        timer = telegraphTime;
    }

    void BeginCharge()
    {
        state = State.Charge;
        chargeTravelled = 0f;
        lastPos = transform.position;
        lane.enabled = false;
        if (lockedDir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(lockedDir);
    }

    void ChargeUpdate()
    {
        desiredVel = lockedDir * chargeSpeed;                    // straight along the locked lane: no steering

        Vector3 pos = transform.position;
        chargeTravelled += new Vector3(pos.x - lastPos.x, 0f, pos.z - lastPos.z).magnitude;
        lastPos = pos;

        if (TryHitPlayer()) { EndCharge(recoverAfterHitTime); return; }
        if (chargeTravelled >= chargeDistance) EndCharge(recoverTime);   // ran out of road: tired
    }

    // Did the charging body touch Shadow? (A sphere around the body: reliable even though
    // Shadow's CharacterController does not take part in normal physics collisions.)
    bool TryHitPlayer()
    {
        Vector3 center = col.bounds.center;
        float radius = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) + hitRadiusPadding;
        int n = Physics.OverlapSphereNonAlloc(center, radius, hitBuf, hitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            ShadowHealth h = hitBuf[i].GetComponentInParent<ShadowHealth>();
            if (h == null) continue;
            // NOTE: if Shadow is mid-warp, ShadowHealth ignores this damage (he is untouchable), but the
            // charge still counts as having connected, so the Rusher stops and recovers. A warp dodges
            // the DAMAGE; it does not make him run past you.
            h.TakeDamage(meleeDamage, h.transform.position + Vector3.up, DamageKind.Enemy);
            return true;
        }
        return false;
    }

    // A wall (or anything solid) in front of him mid-charge: he crashes and is stunned for the full time.
    void OnCollisionEnter(Collision c)
    {
        if (state != State.Charge) return;
        for (int i = 0; i < c.contactCount; i++)
        {
            // Floors have an upward normal, so only things that face back along the charge count.
            if (Vector3.Dot(c.GetContact(i).normal, lockedDir) < -0.5f)
            {
                EndCharge(recoverTime);
                return;
            }
        }
    }

    void EndCharge(float recover)
    {
        state = State.Recover;
        timer = recover;
        desiredVel = Vector3.zero;
    }

    // ---------------------------------------------------------------- telegraph lane
    // A flat red strip on the floor where the charge will run. It is NOT parented to the enemy,
    // so it does not turn with him. It is removed in OnDestroy.
    void BuildLane()
    {
        GameObject go = new GameObject("RusherLane");
        lane = go.AddComponent<LineRenderer>();
        lane.positionCount = 2;
        lane.useWorldSpace = true;
        lane.alignment = LineAlignment.TransformZ;               // the strip faces the transform's Z axis...
        go.transform.rotation = Quaternion.LookRotation(Vector3.up); // ...which we point UP, so it lies flat on the floor
        lane.material = new Material(Shader.Find("Sprites/Default"));
        lane.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lane.enabled = false;
    }

    void DrawLane(bool locked)
    {
        Bounds b = col.bounds;
        float floorY = b.min.y + 0.05f;
        Vector3 start = new Vector3(b.center.x, floorY, b.center.z);
        Vector3 end = start + lockedDir * chargeDistance;

        lane.enabled = true;
        lane.SetPosition(0, start);
        lane.SetPosition(1, end);
        lane.startWidth = lane.endWidth = Mathf.Max(b.extents.x, b.extents.z) * 2f;   // as wide as his body
        Color c = locked ? new Color(1f, 0.15f, 0.1f, 0.75f) : new Color(1f, 0.15f, 0.1f, 0.3f);
        lane.startColor = c;
        lane.endColor = c;
    }

    // ---------------------------------------------------------------- damage
    // IDamageable: pistol, shotgun, spin dash and homing attack all arrive here.
    public void TakeDamage(float amount, Vector3 point, DamageKind kind)
    {
        if (dead) return;
        if (state == State.Recover) amount *= recoverDamageMultiplier;   // caught him out of breath
        health -= amount;
        flashUntil = Time.time + 0.08f;
        if (state == State.Idle) state = State.Approach;                 // being shot wakes him up
        if (health <= 0f) Die(kind);
    }

    // `kind` is how the KILLING blow was dealt: it decides the drop (see EnemyDrops).
    void Die(DamageKind kind)
    {
        dead = true;
        drops.Drop(kind, col.bounds.center);
        Destroy(gameObject);
    }

    // White flash on every hit, and a white pulse during the wind-up so he reads as "about to charge".
    void UpdateVisuals()
    {
        bool flashing = Time.time < flashUntil;
        float pulse = 0f;
        if (state == State.Telegraph) pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
        else if (state == State.Locked) pulse = 1f;

        for (int i = 0; i < rends.Length; i++)
            rends[i].material.color = flashing ? Color.white : Color.Lerp(baseColors[i], Color.white, pulse * 0.7f);
    }
}
