using UnityEngine;

// A hovering GUN drone: keeps its distance, strafes, and fires a telegraphed burst of slow bolts.
// Goes on a Sphere. Adds its own Rigidbody and EnemyDrops. Spin dash can't reach it (it hovers):
// guns and the homing attack are the answers.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EnemyDrops))]
public class GunDrone : MonoBehaviour, IDamageable
{
    [Header("Health (drops are set on the EnemyDrops component)")]
    public float maxHealth = 25f;          // exactly one homing hit (25): change both together

    [Header("Flying")]
    public float aggroRange = 40f;
    public float hoverHeight = 5f;         // metres above whatever is beneath him
    public float preferredRange = 16f;     // horizontal distance he tries to keep
    public float rangeTolerance = 3f;
    public float moveSpeed = 5f;
    public float strafeSpeed = 3.5f;
    public float strafeFlipTime = 2f;      // average seconds between changing strafe direction
    public float verticalSpeed = 3f;       // how fast he corrects his height
    public float turnSpeed = 240f;         // degrees per second

    [Header("Attack (a burst of slow bolts)")]
    public float chargeTime = 0.7f;        // he stops and glows this long before firing: the warning
    public int burstCount = 3;
    public float burstInterval = 0.28f;
    public float burstCooldown = 2.2f;
    public float boltSpeed = 15f;
    public float boltDamage = 10f;
    [Range(0f, 1f)]
    public float leadFactor = 0.5f;        // 0 = aim where you are, 1 = aim at where you will be
    public LayerMask losMask = ~0;         // untick Player here (see setup)

    enum State { Idle, Engage, Charging, Firing }

    State state = State.Idle;
    Rigidbody rb;
    Collider col;
    EnemyDrops drops;
    ShadowHealth player;
    ShadowMotor playerMotor;
    Renderer[] rends;
    Color[] baseColors;

    float health, timer, shotTimer, nextBurstTime, nextFlip, flashUntil;
    float strafeDir = 1f;
    int burstLeft;
    bool dead;
    Vector3 desiredVel;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        drops = GetComponent<EnemyDrops>();
        rb.useGravity = false;                                  // he flies
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        health = maxHealth;
        rends = GetComponentsInChildren<Renderer>();
        baseColors = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++) baseColors[i] = rends[i].material.color;

        nextBurstTime = Time.time + Random.Range(0.8f, 2f);     // a group shouldn't all fire together
        strafeDir = Random.value < 0.5f ? -1f : 1f;
    }

    void Start()
    {
        player = FindAnyObjectByType<ShadowHealth>();
        if (player != null) playerMotor = player.GetComponent<ShadowMotor>();
    }

    void FixedUpdate()
    {
        rb.linearVelocity = desiredVel;   // we set the full 3D velocity: no gravity, no drift
    }

    void Update()
    {
        UpdateVisuals();

        if (dead || player == null || player.IsDead)
        {
            desiredVel = Vector3.zero;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 target = player.transform.position + Vector3.up;   // the player's pivot is at his feet
        Vector3 to = target - pos;
        float dist = to.magnitude;
        bool sees = dist <= aggroRange && HasLineOfSight(pos, to / Mathf.Max(dist, 0.01f), dist);

        Face(to);
        Vector3 hover = Vector3.up * HoverVelocity();

        switch (state)
        {
            case State.Idle:
                desiredVel = hover;
                if (sees) state = State.Engage;
                break;

            case State.Engage:
                desiredVel = Steer(to) + hover;
                if (sees && Time.time >= nextBurstTime) { state = State.Charging; timer = chargeTime; }
                break;

            case State.Charging:
                desiredVel = hover;                         // holds still and glows: readable
                timer -= Time.deltaTime;
                if (timer <= 0f) { state = State.Firing; burstLeft = burstCount; shotTimer = 0f; }
                break;

            case State.Firing:
                desiredVel = hover;
                shotTimer -= Time.deltaTime;
                if (shotTimer <= 0f)
                {
                    FireBolt(pos, target);
                    burstLeft--;
                    shotTimer = burstInterval;
                }
                if (burstLeft <= 0) { state = State.Engage; nextBurstTime = Time.time + burstCooldown; }
                break;
        }
    }

    // Vertical speed that holds him hoverHeight above the ground beneath him.
    float HoverVelocity()
    {
        float groundY = transform.position.y - hoverHeight;   // if nothing is below, keep the current height
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 60f, losMask, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;
        float error = (groundY + hoverHeight) - transform.position.y;
        return Mathf.Clamp(error * 2f, -verticalSpeed, verticalSpeed);
    }

    // Hold a preferred horizontal range and strafe, flipping direction now and then.
    Vector3 Steer(Vector3 to)
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
        return move + Vector3.Cross(Vector3.up, fwd) * (strafeDir * strafeSpeed);
    }

    void Face(Vector3 to)
    {
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (flat.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat),
                                                      turnSpeed * Time.deltaTime);
    }

    bool HasLineOfSight(Vector3 from, Vector3 dir, float dist)
    {
        if (!Physics.Raycast(from, dir, out RaycastHit hit, dist, losMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.GetComponentInParent<ShadowHealth>() != null;
    }

    // ---------------------------------------------------------------- attack
    void FireBolt(Vector3 from, Vector3 target)
    {
        // Lead the target by leadFactor: aim partly at where Shadow is GOING (horizontal velocity only,
        // so a slam or a jump can't make the bolt aim wildly up or down).
        Vector3 v = playerMotor != null ? playerMotor.Velocity : Vector3.zero;
        v.y = 0f;
        float flightTime = Vector3.Distance(from, target) / boltSpeed;
        Vector3 aim = target + v * (flightTime * leadFactor);
        Vector3 dir = (aim - from).normalized;

        GameObject b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(b.GetComponent<Collider>());                    // visual only: the bolt does its own hit tests
        b.transform.position = from + dir * 0.9f;               // spawn just outside the drone's own body
        b.transform.localScale = Vector3.one * 0.4f;
        b.GetComponent<Renderer>().material.color = new Color(1f, 0.55f, 0.1f);
        b.AddComponent<DroneBolt>().Init(dir, boltSpeed, boltDamage);
    }

    // ---------------------------------------------------------------- damage
    // IDamageable: pistol, shotgun and homing attack all arrive here.
    public void TakeDamage(float amount, Vector3 point, DamageKind kind)
    {
        if (dead) return;
        health -= amount;
        flashUntil = Time.time + 0.08f;
        if (state == State.Idle) state = State.Engage;          // being shot wakes him up
        if (health <= 0f) Die(kind);
    }

    void Die(DamageKind kind)
    {
        dead = true;
        drops.Drop(kind, col.bounds.center);
        Destroy(gameObject);
    }

    // White flash on every hit, and an orange pulse while charging so he reads as "about to fire".
    void UpdateVisuals()
    {
        bool flashing = Time.time < flashUntil;
        float pulse = state == State.Charging ? 0.5f + 0.5f * Mathf.Sin(Time.time * 20f) : 0f;
        for (int i = 0; i < rends.Length; i++)
            rends[i].material.color = flashing ? Color.white : Color.Lerp(baseColors[i], new Color(1f, 0.55f, 0.1f), pulse);
    }
}