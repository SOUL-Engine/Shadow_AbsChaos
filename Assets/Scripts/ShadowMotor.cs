using UnityEngine;

// ============================================================================
// ShadowMotor.cs                                                     (v0.4)
// GOES ON: Player (root object; needs CharacterController + ShadowInput too)
//
// JOB: Owns everything about how Shadow MOVES. Three independent things:
//   1. VELOCITY    - where he actually travels (WASD relative to the camera)
//   2. BODY FACING - which way the Visual child points (see UpdateFacing)
//   3. AIM         - where the arm points: always the camera crosshair target
//
// MOVEMENT STATES (simple flags, checked in Update):
//   Grounded / Airborne  - from the CharacterController
//   IsBoosting           - Shift held, costs gauge
//   IsDodging            - Q: a Chaos Control warp (near-instant short-range blink)
//   IsSlamming           - Ctrl in the air: a fast dive (movement tech, no damage)
//
// HIERARCHY THIS SCRIPT EXPECTS:
//   Player                 <- THIS script lives here. NEVER rotates.
//    └ Visual             <- drag into `visual`. Only this child rotates.
//       └ AimArm          <- drag into `aimArm`. Rotation is overridden every frame.
// ============================================================================
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(ShadowInput))]
public class ShadowMotor : MonoBehaviour
{
    // -------------------- Inspector: references --------------------
    [Header("References (drag these in)")]
    public ShadowCameraRig rig;   // the CameraRig object (NOT the Main Camera)
    public Transform visual;      // Player/Visual
    public Transform aimArm;      // Player/Visual/AimArm

    // -------------------- Inspector: tuning --------------------
    [Header("Speed (m/s)")]
    public float walkSpeed = 9f;
    public float boostSpeed = 20f;

    [Header("Acceleration (m/s per second)")]
    public float groundAccel = 60f;  // speeding up on the ground
    public float groundDecel = 45f;  // stopping: snappy, not instant (leaves a short skate tail)
    public float airAccel = 25f;     // air control: lower than ground, never zero

    [Header("Jump")]
    public float jumpHeight = 2.2f;
    public float gravity = -35f;       // stronger than real life = less floaty
    public float coyoteTime = 0.1f;    // can still jump this long after leaving a ledge
    public float jumpBuffer = 0.1f;    // a jump pressed this early before landing still counts
    public float airborneGrace = 0.12f;// time off the ground before IsAirborne turns true
    public float minLandingSpeed = 5f; // slower impacts don't count as a "landing" (no camera dip)

    [Header("Boost Gauge (boost is NOT free)")]
    public float boostMax = 100f;
    public float boostDrainPerSec = 22f;   // ~4.5 s of continuous boost from full
    public float boostRegenPerSec = 20f;   // ~5 s to refill
    public float boostRegenDelay = 0.75f;  // pause after using gauge before refill begins
    [Range(0f, 1f)]
    public float boostUnlockFraction = 0.3f; // after hitting 0 you are locked out until refilled to this

    [Header("Chaos Dodge (Q): a short Chaos Control warp")]
    public float dodgeDistance = 6.5f;  // metres warped. Walls still stop you (no phasing through).
    public float dodgeSpeed = 90f;      // m/s during the warp: 6.5 m takes about 0.07 s, near-instant
    [Range(0f, 1f)]
    public float dodgeExitSpeedFraction = 0.6f; // fraction of boostSpeed carried OUT of the warp
    public float dodgeCooldown = 1.5f;  // seconds from one dodge to the next

    [Header("Chaos Dodge Effects")]
    public float ghostSpacing = 1.6f;   // metres between trail afterimages during the warp
    public float ghostLife = 0.35f;     // seconds an afterimage takes to fade
    public Color chaosColor = new Color(1f, 0.12f, 0.08f, 0.75f); // Shadow red

    [Header("Ground Slam (Ctrl, airborne only)")]
    public float slamSpeed = 55f;           // downward speed during the dive
    public float slamSteerSpeed = 3f;       // small sideways control while diving
    [Range(0f, 1f)]
    public float slamHorizontalKeep = 0.3f; // fraction of sideways speed kept when the dive starts
    public float slamBoostCost = 15f;       // gauge cost (0 = free). Keeps slam from being spammed.
    public float slamJumpMultiplier = 1.5f; // jump height multiplier right after a slam landing...
    public float slamJumpWindow = 0.3f;     // ...if you jump within this many seconds (slam-jump tech)

    [Header("Facing")]
    public float turnSpeed = 900f;          // degrees/second the body can rotate
    [Range(-1f, 1f)]
    public float boostBackwardCutoff = -0.3f; // boosting backwards beyond this keeps facing the camera

    [Header("Aim")]
    public LayerMask aimMask = ~0;    // which layers the crosshair ray can hit. Shadow's own
                                      // colliders are ignored in code, so Everything is fine.
    public float aimMaxDistance = 200f;
    public float minAimDistance = 3f; // closer than this the arm just points parallel to the camera

    // -------------------- Read by other scripts --------------------
    public Vector3 Velocity => new Vector3(horizontalVel.x, verticalVel, horizontalVel.z);
    public Vector2 MoveInput { get; private set; }   // raw WASD: x = right, y = forward
    public bool IsBoosting { get; private set; }
    public bool IsDodging { get; private set; }
    public bool IsSlamming { get; private set; }
    public bool IsAirborne => Time.time - lastGroundedTime > airborneGrace;
    public bool IsBoostLocked => boostLocked;        // used by the HUD (red bar)
    public float BoostNormalized => boost / boostMax; // 0..1
    public Vector3 AimPoint { get; private set; }     // world point under the crosshair

    // One-frame reports, read by the camera.
    public bool LandedThisFrame { get; private set; }
    public float LastImpactSpeed { get; private set; }  // m/s downward at the moment of landing
    public bool DodgeStartedThisFrame { get; private set; }

    // -------------------- Private state --------------------
    CharacterController cc;
    ShadowInput input;
    Renderer[] visualRenderers;              // all meshes under Visual, hidden mid-warp
    readonly RaycastHit[] aimHits = new RaycastHit[16];

    Vector3 horizontalVel;     // X/Z velocity, handled with acceleration
    float verticalVel;         // Y velocity, handled with gravity
    float lastGroundedTime = -10f;
    float lastJumpPressTime = -10f;
    float lastSlamLandTime = -10f;

    float boost;               // current gauge value
    float lastBoostTime = -10f;
    bool boostLocked;

    Vector3 dodgeDir;
    float dodgeRemaining;      // metres of warp still to travel
    float nextDodgeTime;
    Vector3 lastGhostPos;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        input = GetComponent<ShadowInput>();
        visualRenderers = visual.GetComponentsInChildren<Renderer>();
        boost = boostMax;
    }

    // Movement runs in Update. The camera follows in LateUpdate (after us), so no jitter.
    void Update()
    {
        // One-frame flags: cleared every frame, set again below when the event happens.
        LandedThisFrame = false;
        DodgeStartedThisFrame = false;

        // ---- 1. Read input ----
        MoveInput = input.Move;
        bool hasInput = MoveInput.sqrMagnitude > 0.01f;
        if (input.JumpPressed) lastJumpPressTime = Time.time;

        // ---- 2. Camera-relative wish direction ----
        // Only the camera's YAW matters. Looking up/down must never slow you down.
        Quaternion camYaw = Quaternion.Euler(0f, rig.Yaw, 0f);
        Vector3 wishDir = camYaw * new Vector3(MoveInput.x, 0f, MoveInput.y);

        // ---- 3. Start dodge / slam if pressed ----
        if (input.DodgePressed) TryStartDodge(wishDir, hasInput);
        if (input.SlamPressed) TryStartSlam();

        // ---- 4. Boost gauge decides whether we are boosting this frame ----
        UpdateBoost(hasInput);

        // ---- 5. Horizontal movement ----
        bool finishDodge = false;
        if (IsDodging)
        {
            // The warp is DISTANCE-based, not time-based, so it always covers exactly
            // dodgeDistance whatever the frame rate. Each frame we travel `step` metres.
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float step = Mathf.Min(dodgeSpeed * dt, dodgeRemaining);
            horizontalVel = dodgeDir * (step / dt);
            dodgeRemaining -= step;
            finishDodge = dodgeRemaining <= 0.001f;
        }
        else
        {
            float targetSpeed = IsSlamming ? slamSteerSpeed : (IsBoosting ? boostSpeed : walkSpeed);
            Vector3 targetVel = wishDir * targetSpeed;
            float accel = cc.isGrounded ? (hasInput ? groundAccel : groundDecel) : airAccel;
            horizontalVel = Vector3.MoveTowards(horizontalVel, targetVel, accel * Time.deltaTime);
        }

        // ---- 6. Vertical: landing, jump, gravity ----
        bool wasAirborne = IsAirborne; // read BEFORE lastGroundedTime is refreshed below
        if (cc.isGrounded)
        {
            if (wasAirborne) HandleLanding(); // needs verticalVel as it was on impact
            lastGroundedTime = Time.time;
            if (verticalVel < 0f) verticalVel = -2f; // small push keeps us glued to slopes
        }

        bool canJump = Time.time - lastGroundedTime <= coyoteTime && !IsSlamming;
        bool wantsJump = Time.time - lastJumpPressTime <= jumpBuffer;
        if (canJump && wantsJump)
        {
            // Slam-jump: jumping right after a slam landing goes higher (ULTRAKILL-style tech).
            bool slamBounce = Time.time - lastSlamLandTime <= slamJumpWindow;
            float h = slamBounce ? jumpHeight * slamJumpMultiplier : jumpHeight;
            verticalVel = Mathf.Sqrt(2f * -gravity * h); // v = sqrt(2gh): reach exactly h
            lastJumpPressTime = -10f;   // consume the buffered press
            lastGroundedTime = -10f;    // consume coyote time (no double jump)
        }

        if (IsDodging)
            verticalVel = cc.isGrounded ? -2f : 0f; // flat warp (an air dodge holds its height)
        else if (!IsSlamming)
            verticalVel += gravity * Time.deltaTime; // during a slam, verticalVel is held at -slamSpeed

        // ---- 7. Move the CharacterController ----
        cc.Move(Velocity * Time.deltaTime);

        // ---- 8. Dodge follow-ups (after the move, so positions are current) ----
        if (IsDodging) DropTrailAfterimage();
        if (finishDodge) EndDodge();

        // ---- 9. Rotate the body ----
        UpdateFacing();
    }

    // CHAOS DODGE: a short warp in the direction you are pressing. With no input you warp
    // BACKWARD (away from the camera's forward), a desperate retreat. Change `-camFwd` to
    // `camFwd` to flip that.
    void TryStartDodge(Vector3 wishDir, bool hasInput)
    {
        if (IsSlamming || IsDodging || Time.time < nextDodgeTime) return;

        Vector3 camFwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
        dodgeDir = hasInput ? wishDir.normalized : -camFwd;
        dodgeRemaining = dodgeDistance;
        nextDodgeTime = Time.time + dodgeCooldown;
        IsDodging = true;
        DodgeStartedThisFrame = true;

        // "He was HERE": leave an afterimage at the start point, then vanish.
        ShadowAfterimage.Spawn(visual, chaosColor, ghostLife);
        lastGhostPos = transform.position;
        SetVisualVisible(false);
    }

    // Drops a fading copy every `ghostSpacing` metres while warping: the streak.
    void DropTrailAfterimage()
    {
        if ((transform.position - lastGhostPos).sqrMagnitude < ghostSpacing * ghostSpacing) return;
        ShadowAfterimage.Spawn(visual, chaosColor, ghostLife);
        lastGhostPos = transform.position;
    }

    // "Now he's THERE": reappear with a swelling flash and keep a little momentum.
    void EndDodge()
    {
        IsDodging = false;
        horizontalVel = dodgeDir * (boostSpeed * dodgeExitSpeedFraction);
        SetVisualVisible(true);
        ShadowAfterimage.Spawn(visual, Color.Lerp(chaosColor, Color.white, 0.5f), 0.18f, 1.6f);
    }

    void SetVisualVisible(bool visible)
    {
        foreach (Renderer r in visualRenderers) r.enabled = visible;
    }

    // GROUND SLAM: airborne only. Kills most sideways speed and dives straight down.
    // No damage: it is a movement tech. It costs some boost gauge so it can't be spammed.
    void TryStartSlam()
    {
        if (IsSlamming || !IsAirborne || boost < slamBoostCost) return;

        if (IsDodging) { IsDodging = false; SetVisualVisible(true); } // slam cancels a warp

        IsSlamming = true;
        boost -= slamBoostCost;
        lastBoostTime = Time.time;         // also delays gauge regen, like boosting does
        verticalVel = -slamSpeed;
        horizontalVel *= slamHorizontalKeep;
    }

    // Called the frame we touch the ground after being airborne.
    // verticalVel is still the impact speed here (it is reset to -2 right after).
    void HandleLanding()
    {
        float impact = -verticalVel;

        if (IsSlamming)
        {
            IsSlamming = false;
            lastSlamLandTime = Time.time;   // opens the slam-jump window
            LandedThisFrame = true;
            LastImpactSpeed = slamSpeed;    // biggest possible camera dip
        }
        else if (impact > minLandingSpeed)
        {
            LandedThisFrame = true;
            LastImpactSpeed = impact;
        }
    }

    // Boost rules: costs gauge while held, refills after a delay, locks out if you empty it.
    void UpdateBoost(bool hasInput)
    {
        // Lockout ends once the gauge has refilled enough.
        if (boostLocked && boost >= boostMax * boostUnlockFraction) boostLocked = false;

        IsBoosting = input.BoostHeld && hasInput && !boostLocked && boost > 0f
                     && !IsSlamming && !IsDodging;

        if (IsBoosting)
        {
            boost -= boostDrainPerSec * Time.deltaTime;
            lastBoostTime = Time.time;
            if (boost <= 0f) { boost = 0f; boostLocked = true; IsBoosting = false; }
        }
        else if (Time.time - lastBoostTime >= boostRegenDelay)
        {
            boost = Mathf.Min(boostMax, boost + boostRegenPerSec * Time.deltaTime);
        }
    }

    // FACING RULES (only the Visual child rotates):
    //   Strafing / standing / skating backward / dodging / slamming -> body faces the CAMERA yaw
    //   Boosting (not backward)                                      -> body faces the VELOCITY
    void UpdateFacing()
    {
        float targetYaw = rig.Yaw; // default: face where the camera looks

        if (IsBoosting && horizontalVel.sqrMagnitude > 1f) // >1 avoids atan2(0,0) snapping north
        {
            Vector3 camFwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
            bool boostingBackward = Vector3.Dot(horizontalVel.normalized, camFwd) < boostBackwardCutoff;
            if (!boostingBackward)
                targetYaw = Mathf.Atan2(horizontalVel.x, horizontalVel.z) * Mathf.Rad2Deg;
        }

        float yaw = Mathf.MoveTowardsAngle(visual.eulerAngles.y, targetYaw, turnSpeed * Time.deltaTime);
        visual.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // AIM: the arm points at whatever is under the crosshair.
    // Called BY ShadowCameraRig at the end of ITS LateUpdate, so the camera has already
    // moved this frame. (Doing this in our own Update would lag the arm one frame behind.)
    public void UpdateAim()
    {
        Transform camT = rig.cam.transform;

        if (RaycastIgnoringSelf(camT.position, camT.forward, aimMaxDistance, out RaycastHit hit))
            AimPoint = hit.point;
        else
            AimPoint = camT.position + camT.forward * aimMaxDistance;

        // The arm sits beside Shadow, not at the camera, so point it at the AimPoint.
        Vector3 toAim = AimPoint - aimArm.position;
        aimArm.rotation = toAim.sqrMagnitude > minAimDistance * minAimDistance
            ? Quaternion.LookRotation(toAim)
            : camT.rotation; // too close: avoid wild swinging
    }

    // Nearest hit that is NOT part of Shadow himself (the Player object or any child).
    // This makes aiming independent of layer setup: his own capsule, face cube or arm
    // can never block the crosshair ray.
    bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, aimHits, maxDist, aimMask,
                                        QueryTriggerInteraction.Ignore);
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (aimHits[i].collider.transform.IsChildOf(transform)) continue; // skip ourselves
            if (aimHits[i].distance < bestDist)
            {
                bestDist = aimHits[i].distance;
                best = aimHits[i];
                found = true;
            }
        }
        return found;
    }
}