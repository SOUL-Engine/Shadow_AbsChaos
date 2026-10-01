using UnityEngine;

// ============================================================================
// GunGrunt.cs                                                        (v0.8)
// GOES ON: a Capsule (the capsule primitive already has the CapsuleCollider this needs).
//   The script adds a Rigidbody for you. Scale/colour the capsule so it is easy to spot:
//   a distinct colour (GUN blue, say) means you read "enemy" within one second.
//   Put it on the DEFAULT layer, NOT Player.
//
// JOB: The first sandbox enemy: a GUN soldier who keeps his distance, strafes,
// and fires a TELEGRAPHED hitscan shot.
//
// WHY THIS DESIGN (boomer shooter checklist):
//   * Readable: a red laser line locks onto you, then turns white-hot, then fires.
//   * Fair: the line follows you for `telegraphTime`, then FREEZES for `lockTime`. If you
//     move (or warp) after it freezes, it misses. Standing still and eating it is your fault.
//   * Tests the kit: the warp, strafing, closing the distance with a spin dash, and
//     homing attacks on a stationary-while-aiming target.
//   * Weak on purpose (40 HP: 4 pistol shots, 2 homing hits). A cheap enemy in a good
//     place beats a damage sponge.
//
// Takes damage through IDamageable, so the pistol, shotgun, spin dash, ball dash and
// homing attack all work on it without extra code.
// ============================================================================
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class GunGrunt : MonoBehaviour, IDamageable
{
    [Header("Health and drops")]
    public float maxHealth = 40f;
    [Range(0f, 1f)]
    public float healthDropChance = 0.25f; // chance of a health pickup on death
    public float healthDropAmount = 10f;

    [Header("Movement (simple steering, no NavMesh yet)")]
    public float moveSpeed = 4.5f;
    public float preferredRange = 14f;     // tries to hold roughly this distance
    public float rangeTolerance = 3f;
    public float strafeSpeed = 3f;
    public float strafeFlipTime = 1.6f;    // average seconds between changing strafe direction
    public float turnSpeed = 360f;         // degrees per second

    [Header("Attack (telegraphed hitscan)")]
    public float aggroRange = 35f;
    public float telegraphTime = 0.8f;     // the red line TRACKS you for this long...
    public float lockTime = 0.2f;          // ...then freezes (white-hot) for this long, then fires
    public float fireCooldown = 1.4f;
    public float damage = 12f;
    public float maxShotRange = 60f;
    public LayerMask shotMask = ~0;

    enum State { Idle, Engage, Telegraph, Locked }

    State state = State.Idle;
    Rigidbody rb;
    CapsuleCollider col;
    ShadowHealth player;
    LineRenderer laser;
    Renderer[] rends;
    Color[] baseColors;

    float health;
    bool dead;
    Vector3 desiredVel;
    Vector3 lockedDir;
    float stateTimer, nextShotTime, nextFlip, flashUntil;
    float strafeDir = 1f;
    bool wasFlashing;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<CapsuleCollider>();
        rb.freezeRotation = true;                              // he turns by script, physics must not tip him over
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        health = maxHealth;
        rends = GetComponentsInChildren<Renderer>();
        baseColors = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++) baseColors[i] = rends[i].material.color;

        BuildLaser();
        nextShotTime = Time.time + Random.Range(0.5f, 1.5f);   // so a group doesn't all fire on the same frame
        strafeDir = Random.value < 0.5f ? -1f : 1f;
    }

    void Start()
    {
        player = FindFirstObjectByType<ShadowHealth>();
    }

    // ---------------------------------------------------------------- AI
    void Update()
    {
        UpdateFlash();

        if (dead || player == null || player.IsDead)
        {
            desiredVel = Vector3.zero;
            laser.enabled = false;
            return;
        }

        Vector3 chest = col.bounds.center;
        Vector3 target = player.transform.position + Vector3.up;   // the player's pivot is at his feet
        Vector3 to = target - chest;
        float dist = to.magnitude;
        bool sees = dist <= aggroRange && HasLineOfSight(chest, to / Mathf.Max(dist, 0.01f), dist);

        FacePlayer(to);

        switch (state)
        {
            case State.Idle:
                desiredVel = Vector3.zero;
                if (sees) state = State.Engage;
                break;

            case State.Engage:
                desiredVel = SteerAround(to);
                if (sees && Time.time >= nextShotTime) BeginTelegraph();
                break;

            case State.Telegraph:
                desiredVel = Vector3.zero;                      // stands still to aim: readable wind-up
                lockedDir = to / Mathf.Max(dist, 0.01f);        // the line tracks you
                DrawLaser(chest, lockedDir, false);
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) { state = State.Locked; stateTimer = lockTime; }
                break;

            case State.Locked:
                desiredVel = Vector3.zero;
                DrawLaser(chest, lockedDir, true);              // frozen and white-hot: MOVE NOW
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) Fire(chest);
                break;
        }
    }

    void FixedUpdate()
    {
        // Horizontal movement from the AI, vertical left to gravity.
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(desiredVel.x, v.y, desiredVel.z);
    }

    // Hold a preferred range and strafe sideways, flipping direction now and then.
    Vector3 SteerAround(Vector3 to)
    {
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        float fd = flat.magnitude;
        if (fd < 0.01f) return Vector3.zero;
        Vector3 fwd = flat / fd;

        Vector3 move = Vector3.zero;
        if (fd > preferredRange + rangeTolerance) move += fwd * moveSpeed;
        else if (fd < preferredRange - rangeTolerance) move -= fwd * moveSpeed;

        if (Time.time >= nextFlip)
        {
            strafeDir = Random.value < 0.5f ? -1f : 1f;
            nextFlip = Time.time + strafeFlipTime * Random.Range(0.7f, 1.3f);
        }
        move += Vector3.Cross(Vector3.up, fwd) * (strafeDir * strafeSpeed);
        return move;
    }

    void FacePlayer(Vector3 to)
    {
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (flat.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat),
                                                      turnSpeed * Time.deltaTime);
    }

    bool HasLineOfSight(Vector3 from, Vector3 dir, float dist)
    {
        // The first thing in the way must be the player (or nothing). Starting inside our own
        // collider means we never hit ourselves.
        if (!Physics.Raycast(from, dir, out RaycastHit hit, dist, shotMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.GetComponentInParent<ShadowHealth>() != null;
    }

    // ---------------------------------------------------------------- attack
    void BeginTelegraph()
    {
        state = State.Telegraph;
        stateTimer = telegraphTime;
    }

    void Fire(Vector3 origin)
    {
        Vector3 end = origin + lockedDir * maxShotRange;
        if (Physics.Raycast(origin, lockedDir, out RaycastHit hit, maxShotRange, shotMask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            ShadowHealth h = hit.collider.GetComponentInParent<ShadowHealth>();
            if (h != null) h.TakeDamage(damage, hit.point);   // warping / moving after the lock = a clean miss
        }
        SpawnShotLine(origin, end);

        laser.enabled = false;
        state = State.Engage;
        nextShotTime = Time.time + fireCooldown;
    }

    // ---------------------------------------------------------------- laser telegraph
    void BuildLaser()
    {
        GameObject go = new GameObject("Laser");
        go.transform.SetParent(transform, false);
        laser = go.AddComponent<LineRenderer>();
        laser.positionCount = 2;
        laser.useWorldSpace = true;
        laser.material = new Material(Shader.Find("Sprites/Default"));
        laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laser.enabled = false;
    }

    void DrawLaser(Vector3 origin, Vector3 dir, bool locked)
    {
        float len = maxShotRange;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxShotRange, shotMask, QueryTriggerInteraction.Ignore))
            len = hit.distance;

        laser.enabled = true;
        laser.SetPosition(0, origin);
        laser.SetPosition(1, origin + dir * len);
        // Red and thin while it tracks you; thick and white-hot once it has locked.
        Color c = locked ? new Color(1f, 0.95f, 0.85f, 1f) : new Color(1f, 0.1f, 0.1f, 0.6f);
        laser.startColor = c;
        laser.endColor = c;
        laser.startWidth = laser.endWidth = locked ? 0.12f : 0.04f;
    }

    // A short bright line for the shot itself (prototype visual: replace with a proper effect later).
    void SpawnShotLine(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        if (d.magnitude < 0.05f) return;
        GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(t.GetComponent<Collider>());
        t.transform.position = from + d * 0.5f;
        t.transform.rotation = Quaternion.LookRotation(d);
        t.transform.localScale = new Vector3(0.08f, 0.08f, d.magnitude);
        t.GetComponent<Renderer>().material.color = new Color(1f, 0.5f, 0.2f);
        Destroy(t, 0.08f);
    }

    // ---------------------------------------------------------------- damage
    // IDamageable: pistol, shotgun, spin dash, ball dash and homing attack all arrive here.
    public void TakeDamage(float amount, Vector3 point)
    {
        if (dead) return;
        health -= amount;
        flashUntil = Time.time + 0.08f;
        if (state == State.Idle) state = State.Engage;         // being shot wakes him up
        if (health <= 0f) Die();
    }

    void Die()
    {
        dead = true;
        if (Random.value <= healthDropChance) HealthPickup.Spawn(col.bounds.center, healthDropAmount);
        Destroy(gameObject);
    }

    // White flash on every hit so you can SEE your damage land.
    void UpdateFlash()
    {
        bool flashing = Time.time < flashUntil;
        if (flashing == wasFlashing) return;
        wasFlashing = flashing;
        for (int i = 0; i < rends.Length; i++)
            rends[i].material.color = flashing ? Color.white : baseColors[i];
    }
}
