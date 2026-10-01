using UnityEngine;

// ============================================================================
// ShadowMotor.cs  (CORE)                                             (v0.5)
// GOES ON: Player (root object; needs CharacterController + ShadowInput too)
//
// The motor is split into FOUR files (one C# "partial class"), so each ability
// lives in its own readable file. Put all four in Assets/Shadow/Scripts/:
//   ShadowMotor.cs           <- THIS FILE: update order, boost, dodge, slam, landing, facing, aim
//   ShadowMotor_Slide.cs     <- slide + spin dash (Ctrl on the ground)
//   ShadowMotor_AirDash.cs   <- homing attack + ball dash (Jump again in the air)
//   ShadowMotor_Wall.cs      <- ledge mantle, wall run, wall jump
// Attach only ShadowMotor to the Player. The other three are part of it.
//
// JOB: Owns everything about how Shadow MOVES. Three independent things:
//   1. VELOCITY    - where he actually travels
//   2. BODY FACING - which way the Visual child points
//   3. AIM         - where the arm points: always the camera crosshair target
//
// MODES: Shadow is always in exactly ONE mode (enum below). A mode decides who
// controls his velocity that frame. Normal = WASD/jump/boost as before.
//
// HIERARCHY THIS SCRIPT EXPECTS:
//   Player                 <- THIS script lives here. NEVER rotates.
//    └ Visual             <- drag into `visual`. Only this child rotates/scales.
//       └ AimArm          <- drag into `aimArm`. Rotation is overridden every frame.
// ============================================================================
public enum ShadowMoveMode
{
    Normal,    // walking / boosting / jumping / falling
    Slide,     // Ctrl on the ground
    SpinDash,  // Ctrl + boost on the ground (ball form, damages, can't shoot)
    Dodge,     // Q: Chaos warp (invulnerable)
    Slam,      // Ctrl in the air
    BallDash,  // Jump again in the air: homing attack / ball dash (damages, can't shoot)
    WallRun,   // timed run along a wall
    Mantle     // pulling up onto a ledge
}

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(ShadowInput))]
public partial class ShadowMotor : MonoBehaviour
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

    [Header("Air Momentum (after slide-jumps, wall jumps, dashes)")]
    public float airSteerDegPerSec = 140f; // while carrying momentum you STEER it instead of braking
    public float carryDrag = 2f;           // gentle slowdown toward walkSpeed (m/s per second)

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
    public int maxDodgeCharges = 5;         // warps stored (like Doom Eternal's dash charges)
    public float dodgeUseCooldown = 0.2f;   // minimum gap between two warps
    public float dodgeRechargeTime = 1.2f;  // seconds for ONE charge to come back (they return one at a time)
    public float dodgeInvulnTail = 0.12f; // extra invulnerable time AFTER reappearing (forgiving window)

    [Header("Invulnerability (decide these in the sandbox, once enemies shoot back)")]
    public bool spinDashInvulnerable = false; // Sonic-style protected ball form?
    public bool ballDashInvulnerable = false; // protect the homing attack / ball dash too?

    [Header("Chaos Effects (afterimages, shared by dodge and ball dash)")]
    public float ghostSpacing = 1.6f;   // metres between trail afterimages
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
    public float spinDegPerSec = 1080f;     // placeholder ball spin speed (the real model will animate this)

    [Header("Aim / World Queries")]
    public LayerMask aimMask = ~0;    // layers the crosshair ray, lock-on and headroom checks can see.
                                      // Shadow's own colliders are ignored in code, so Everything is fine.
    public float aimMaxDistance = 200f;
    public float minAimDistance = 3f; // closer than this the arm just points parallel to the camera

    // -------------------- Read by other scripts --------------------
    public ShadowMoveMode Mode { get; private set; } = ShadowMoveMode.Normal;
    public Vector3 Velocity => new Vector3(horizontalVel.x, verticalVel, horizontalVel.z);
    public Vector2 MoveInput { get; private set; }   // raw WASD: x = right, y = forward
    public bool IsBoosting { get; private set; }
    public bool IsDodging => Mode == ShadowMoveMode.Dodge;
    public bool IsSlamming => Mode == ShadowMoveMode.Slam;
    public bool IsAirborne => Time.time - lastGroundedTime > airborneGrace;
    public bool IsBoostLocked => boostLocked;        // used by the HUD (red bar)
    public float BoostNormalized => boost / boostMax; // 0..1
    public Vector3 AimPoint { get; private set; }     // world point under the crosshair

    // The weapon checks this: no shooting in ball forms or while pulling up onto a ledge.
    public bool CanShoot => Mode != ShadowMoveMode.SpinDash
                         && Mode != ShadowMoveMode.BallDash
                         && Mode != ShadowMoveMode.Mantle;

    // The health system (not built yet) must check this before applying ANY damage.
    public bool IsInvulnerable => IsDodging || Time.time < invulnUntil
                               || (spinDashInvulnerable && Mode == ShadowMoveMode.SpinDash)
                               || (ballDashInvulnerable && Mode == ShadowMoveMode.BallDash);

    // One-frame reports, read by the camera.
    public bool LandedThisFrame { get; private set; }
    public float LastImpactSpeed { get; private set; }  // m/s downward at the moment of landing
    public bool DodgeStartedThisFrame { get; private set; }
    public bool BallDashStartedThisFrame { get; private set; }
    public bool HomingHitThisFrame { get; private set; }
    public bool HomingStartedThisFrame { get; private set; }

    // Warp charges, for the HUD.
    public bool DebugGrounded => cc.isGrounded; // the HUD shows AIR when this is false: reveals ground-contact flicker
    public int DodgeCharges => dodgeCharges;
    public int MaxDodgeCharges => maxDodgeCharges;
    public float DodgeRechargeProgress => dodgeCharges >= maxDodgeCharges ? 1f : Mathf.Clamp01(dodgeRecharge / dodgeRechargeTime);

    // -------------------- Private state (shared by all four files) --------------------
    CharacterController cc;
    ShadowInput input;
    Renderer[] visualRenderers;              // all meshes under Visual, hidden mid-warp
    readonly RaycastHit[] aimHits = new RaycastHit[16];
    readonly Collider[] overlapBuf = new Collider[24];

    Vector3 horizontalVel;     // X/Z velocity
    float verticalVel;         // Y velocity
    float lastGroundedTime = -10f;     // used for coyote time / IsAirborne (jump overwrites it)
    float lastTouchGroundTime = -10f;  // the last time we were truly on the ground (never overwritten)
    float lastJumpPressTime = -10f;
    float lastSlamLandTime = -10f;
    bool carryMomentum;        // true after slide-jumps, wall jumps, dashes: air steering keeps speed

    float boost;               // current gauge value
    float lastBoostTime = -10f;
    bool boostLocked;

    Vector3 dodgeDir;
    float dodgeRemaining;      // metres of warp still to travel
    float nextDodgeTime;
    int dodgeCharges;          // warps available right now
    float dodgeRecharge;       // seconds toward the next charge
    float invulnUntil;         // Time.time until which the post-warp tail protects us
    Vector3 lastGhostPos;

    float bodyYaw;             // the yaw we rotate the Visual to (tracked so spin can't confuse it)
    float spinAngle;           // placeholder spin for ball forms
    Vector3 visualBasePos, visualBaseScale;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        input = GetComponent<ShadowInput>();
        visualRenderers = visual.GetComponentsInChildren<Renderer>();
        boost = boostMax;
        dodgeCharges = maxDodgeCharges;

        standingHeight = cc.height;              // remembered so slide can shrink and restore it
        bodyYaw = visual.eulerAngles.y;
        visualBasePos = visual.localPosition;
        visualBaseScale = visual.localScale;
        ResetAirCounters();
    }

    // Movement runs in Update. The camera follows in LateUpdate (after us), so no jitter.
    void Update()
    {
        // One-frame flags: cleared every frame, set again below when the event happens.
        LandedThisFrame = false;
        DodgeStartedThisFrame = false;
        BallDashStartedThisFrame = false;
        HomingHitThisFrame = false;
        HomingStartedThisFrame = false;

        // ---- 1. Read input ----
        MoveInput = input.Move;
        bool hasInput = MoveInput.sqrMagnitude > 0.01f;
        bool jumpPressed = input.JumpPressed;
        if (jumpPressed) lastJumpPressTime = Time.time;

        // ---- 2. Camera-relative wish direction ----
        // Only the camera's YAW matters. Looking up/down must never slow you down.
        Quaternion camYaw = Quaternion.Euler(0f, rig.Yaw, 0f);
        Vector3 wishDir = camYaw * new Vector3(MoveInput.x, 0f, MoveInput.y);

        // ---- 3. Ground bookkeeping: landings, timers, refills ----
        bool wasAirborne = IsAirborne; // read BEFORE lastGroundedTime is refreshed below
        // (While ball-dashing we ignore ground contact so a homing attack at a ground-level enemy isn't cut short.)
        bool groundedNow = cc.isGrounded && Mode != ShadowMoveMode.BallDash;
        if (groundedNow)
        {
            if (wasAirborne) HandleLanding(); // needs verticalVel as it was on impact
            lastGroundedTime = Time.time;
            lastTouchGroundTime = Time.time;
            if (verticalVel < 0f) verticalVel = -2f; // small push keeps us glued to slopes
            ResetAirCounters();                      // dash, wall runs, wall jumps refill on the ground
        }

        // ---- 3b. Resources that refill over time ----
        UpdateDodgeCharges();
        UpdateHomingChain();

        // ---- 4. Ground jump (also jumps out of a slide / spin dash) ----
        bool jumpFired = TryGroundJump();

        // ---- 5. Look for a homing target (only matters while airborne) ----
        UpdateHomingTarget();

        // ---- 6. Button abilities ----
        if (input.DodgePressed) TryStartDodge(wishDir, hasInput);
        if (input.SlamPressed)
        {
            TryStartSlam();                                  // airborne: dive
            if (!jumpFired) TryStartSlide(wishDir, hasInput); // grounded: slide / spin dash
            // Neither started this frame (ground contact can flicker for a frame)? Keep trying
            // for a moment while Ctrl is held, so a slide is never lost to a one-frame hiccup.
            if (Mode == ShadowMoveMode.Normal) slideBufferedUntil = Time.time + slideBufferTime;
        }
        else if (Mode == ShadowMoveMode.Normal && Time.time <= slideBufferedUntil && input.SlamHeld && !jumpFired)
        {
            TryStartSlide(wishDir, hasInput);
        }

        // ---- 7. Automatic airborne abilities: pull up onto a ledge, else start a wall run ----
        if (Mode == ShadowMoveMode.Normal && IsAirborne && !jumpFired)
        {
            if (!TryStartMantle(wishDir, hasInput)) TryStartWallRun(hasInput);
        }

        // ---- 8. Jump pressed again in the air: wall jump / homing attack / ball dash ----
        if (jumpPressed && !jumpFired) TryAirPressAbility();

        // ---- 9. Boost gauge decides whether we are boosting this frame ----
        UpdateBoost(hasInput);

        // ---- 10. Horizontal velocity: whichever mode is active decides ----
        bool finishDodge = false;
        switch (Mode)
        {
            case ShadowMoveMode.Dodge:
            {
                // The warp is DISTANCE-based, not time-based, so it always covers exactly
                // dodgeDistance whatever the frame rate. Each frame we travel `step` metres.
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                float step = Mathf.Min(dodgeSpeed * dt, dodgeRemaining);
                horizontalVel = dodgeDir * (step / dt);
                dodgeRemaining -= step;
                finishDodge = dodgeRemaining <= 0.001f;
                break;
            }
            case ShadowMoveMode.Slide:
            case ShadowMoveMode.SpinDash: SlideStep(wishDir, hasInput); break;
            case ShadowMoveMode.BallDash:  BallDashStep(); break;
            case ShadowMoveMode.WallRun:   WallRunStep(wishDir, hasInput); break;
            case ShadowMoveMode.Mantle:    MantleStep(); break;
            default:                       NormalHorizontal(wishDir, hasInput); break; // Normal and Slam
        }

        // ---- 11. Vertical velocity: gravity, unless the mode drives it itself ----
        switch (Mode)
        {
            case ShadowMoveMode.Dodge:
                verticalVel = cc.isGrounded ? -2f : 0f; // flat warp (an air dodge holds its height)
                break;
            case ShadowMoveMode.Slam:      // held at -slamSpeed
            case ShadowMoveMode.BallDash:  // set by the dash
            case ShadowMoveMode.Mantle:    // set by the pull-up path
            case ShadowMoveMode.WallRun:   // set by the wall run
                break;
            default:
                verticalVel += gravity * Time.deltaTime;
                break;
        }

        // ---- 12. Move the CharacterController ----
        CollisionFlags flags = cc.Move(Velocity * Time.deltaTime);

        // ---- 13. Follow-ups that need the new position ----
        switch (Mode)
        {
            case ShadowMoveMode.Dodge:
                DropTrailAfterimage();
                if (finishDodge) EndDodge();
                break;
            case ShadowMoveMode.BallDash: BallDashPostMove(flags); break;
            case ShadowMoveMode.Mantle:   MantlePostMove(); break;
        }

        // ---- 14. Rotate / pose the body ----
        UpdateFacing();
    }

    // ---------------- Normal + Slam horizontal movement ----------------
    void NormalHorizontal(Vector3 wishDir, bool hasInput)
    {
        bool inAir = !cc.isGrounded || verticalVel > 0.1f;
        float speed = horizontalVel.magnitude;

        // CARRIED MOMENTUM (after slide-jumps, wall jumps, dashes): don't brake to walk speed,
        // just let the player STEER the speed they have. Drag eases it down slowly.
        if (carryMomentum && inAir && Mode == ShadowMoveMode.Normal && speed > walkSpeed + 0.5f)
        {
            Vector3 dir = hasInput ? SteerDir(horizontalVel, wishDir, airSteerDegPerSec)
                                   : horizontalVel / speed;
            speed = Mathf.Max(walkSpeed, speed - carryDrag * Time.deltaTime);
            horizontalVel = dir * speed;
            return;
        }

        float targetSpeed = Mode == ShadowMoveMode.Slam ? slamSteerSpeed
                          : (IsBoosting ? boostSpeed : walkSpeed);
        Vector3 targetVel = wishDir * targetSpeed;
        float accel = cc.isGrounded ? (hasInput ? groundAccel : groundDecel) : airAccel;
        // Just after a homing hit the player only gets a fraction of their air control, so the
        // bounce plays out the same way every time (see homingResolveTime).
        if (Time.time < homingResolveUntil) accel *= homingResolveControl;
        horizontalVel = Vector3.MoveTowards(horizontalVel, targetVel, accel * Time.deltaTime);
    }

    // Turns a horizontal direction toward a target direction at a limited rate (degrees/second).
    // Works in yaw angles, so a 180-degree reversal can't flip into the vertical plane.
    static Vector3 SteerDir(Vector3 current, Vector3 target, float degPerSec)
    {
        float cur = Mathf.Atan2(current.x, current.z) * Mathf.Rad2Deg;
        float tgt = Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg;
        float next = Mathf.MoveTowardsAngle(cur, tgt, degPerSec * Time.deltaTime) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(next), 0f, Mathf.Cos(next));
    }

    // Resets everything that refills when Shadow touches the ground.
    void ResetAirCounters()
    {
        homingChain = 0;            // a chain ends when you touch down
        lastHomingTarget = null;
        wallRunsLeft = maxWallRunsPerAir;
        wallJumpsLeft = maxWallJumpsPerAir;
        carryMomentum = false;
    }

    // ---------------- Jump ----------------
    // Returns true if a jump actually happened this frame.
    bool TryGroundJump()
    {
        bool groundedMode = Mode == ShadowMoveMode.Normal
                         || Mode == ShadowMoveMode.Slide
                         || Mode == ShadowMoveMode.SpinDash;
        bool canJump = groundedMode && Time.time - lastGroundedTime <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressTime <= jumpBuffer;
        if (!canJump || !wantsJump) return false;

        if (Mode != ShadowMoveMode.Normal)
        {
            if (!HasHeadroom()) return false; // can't stand up here: stay sliding
            ExitSlide(true);                  // stand up, keep speed (slide-jump)
        }

        // Slam-jump: jumping right after a slam landing goes higher (ULTRAKILL-style tech).
        bool slamBounce = Time.time - lastSlamLandTime <= slamJumpWindow;
        float h = slamBounce ? jumpHeight * slamJumpMultiplier : jumpHeight;
        verticalVel = Mathf.Sqrt(2f * -gravity * h); // v = sqrt(2gh): reach exactly h
        lastJumpPressTime = -10f;   // consume the buffered press
        lastGroundedTime = -10f;    // consume coyote time (no double jump)
        return true;
    }

    // ---------------- Dodge ----------------
    // Charges come back ONE AT A TIME: while below the maximum, a timer counts up and
    // returns one charge every dodgeRechargeTime seconds.
    void UpdateDodgeCharges()
    {
        if (dodgeCharges >= maxDodgeCharges) { dodgeRecharge = 0f; return; }

        dodgeRecharge += Time.deltaTime;
        if (dodgeRecharge >= dodgeRechargeTime)
        {
            dodgeRecharge -= dodgeRechargeTime;
            dodgeCharges++;
        }
    }

    // CHAOS DODGE: a short warp in the direction you are pressing. With no input you warp
    // BACKWARD (away from the camera's forward), a desperate retreat. Change `-camFwd` to
    // `camFwd` to flip that.
    void TryStartDodge(Vector3 wishDir, bool hasInput)
    {
        if (dodgeCharges <= 0 || Time.time < nextDodgeTime) return;

        if (Mode == ShadowMoveMode.Slide || Mode == ShadowMoveMode.SpinDash)
        {
            if (!HasHeadroom()) return; // can't stand up here
            ExitSlide(false);
        }
        else if (Mode != ShadowMoveMode.Normal) return;

        Vector3 camFwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
        dodgeDir = hasInput ? wishDir.normalized : -camFwd;
        dodgeRemaining = dodgeDistance;
        dodgeCharges--;
        nextDodgeTime = Time.time + dodgeUseCooldown;
        Mode = ShadowMoveMode.Dodge;
        carryMomentum = false;
        DodgeStartedThisFrame = true;

        // "He was HERE": leave an afterimage at the start point, then vanish.
        ShadowAfterimage.Spawn(visual, chaosColor, ghostLife);
        lastGhostPos = transform.position;
        SetVisualVisible(false);
    }

    // Drops a fading copy every `ghostSpacing` metres while warping/dashing: the streak.
    void DropTrailAfterimage()
    {
        if ((transform.position - lastGhostPos).sqrMagnitude < ghostSpacing * ghostSpacing) return;
        ShadowAfterimage.Spawn(visual, chaosColor, ghostLife);
        lastGhostPos = transform.position;
    }

    // "Now he's THERE": reappear with a swelling flash and keep a little momentum.
    void EndDodge()
    {
        Mode = ShadowMoveMode.Normal;
        horizontalVel = dodgeDir * (boostSpeed * dodgeExitSpeedFraction);
        invulnUntil = Time.time + dodgeInvulnTail;
        SetVisualVisible(true);
        ShadowAfterimage.Spawn(visual, Color.Lerp(chaosColor, Color.white, 0.5f), 0.18f, 1.6f);
    }

    void SetVisualVisible(bool visible)
    {
        foreach (Renderer r in visualRenderers) r.enabled = visible;
    }

    // ---------------- Ground slam ----------------
    // GROUND SLAM: airborne only. Kills most sideways speed and dives straight down.
    // No damage: it is a movement tech. It costs some boost gauge so it can't be spammed.
    // (Can also be used to drop off a wall run.)
    void TryStartSlam()
    {
        if (!IsAirborne || boost < slamBoostCost) return;

        if (Mode == ShadowMoveMode.WallRun) EndWallRun(false);
        else if (Mode != ShadowMoveMode.Normal) return;

        Mode = ShadowMoveMode.Slam;
        carryMomentum = false;
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

        if (Mode == ShadowMoveMode.Slam)
        {
            Mode = ShadowMoveMode.Normal;
            lastSlamLandTime = Time.time;   // opens the slam-jump window
            LandedThisFrame = true;
            LastImpactSpeed = slamSpeed;    // biggest possible camera dip
        }
        else if (impact > minLandingSpeed)
        {
            LandedThisFrame = true;
            LastImpactSpeed = impact;
        }

        if (Mode == ShadowMoveMode.WallRun) Mode = ShadowMoveMode.Normal; // touching the floor ends a wall run
    }

    // ---------------- Boost ----------------
    // Boost rules: costs gauge while held, refills after a delay, locks out if you empty it.
    void UpdateBoost(bool hasInput)
    {
        // Lockout ends once the gauge has refilled enough.
        if (boostLocked && boost >= boostMax * boostUnlockFraction) boostLocked = false;

        IsBoosting = input.BoostHeld && hasInput && !boostLocked && boost > 0f
                     && Mode == ShadowMoveMode.Normal;

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

    // Wipes all movement state. Used when Shadow respawns in place after dying.
    public void ResetMotion()
    {
        horizontalVel = Vector3.zero;
        verticalVel = 0f;
        Mode = ShadowMoveMode.Normal;
        SetControllerHeight(standingHeight);
        SetVisualVisible(true);
        ResetAirCounters();
        boost = boostMax;
        boostLocked = false;
        dodgeCharges = maxDodgeCharges;
        dodgeRecharge = 0f;
        invulnUntil = 0f;
    }

    // ---------------- Facing and body pose ----------------
    // FACING RULES (only the Visual child rotates):
    //   Normal / slide / dodge / slam -> camera yaw (combat default: you can always aim)
    //   Boosting (not backward)       -> the VELOCITY (Sonic forward lean)
    //   Spin dash / ball dash         -> the travel direction (ball form)
    //   Wall run                      -> along the wall
    //   Mantle                        -> toward the ledge
    void UpdateFacing()
    {
        float targetYaw = rig.Yaw; // default: face where the camera looks

        switch (Mode)
        {
            case ShadowMoveMode.SpinDash: targetYaw = YawOf(slideDir, targetYaw); break;
            case ShadowMoveMode.BallDash: targetYaw = YawOf(ballDir, targetYaw); break;
            case ShadowMoveMode.WallRun:  targetYaw = YawOf(wallTangent, targetYaw); break;
            case ShadowMoveMode.Mantle:   targetYaw = YawOf(mantleInward, targetYaw); break;
            default:
                if (IsBoosting && horizontalVel.sqrMagnitude > 1f) // >1 avoids atan2(0,0) snapping north
                {
                    Vector3 camFwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
                    bool boostingBackward = Vector3.Dot(horizontalVel.normalized, camFwd) < boostBackwardCutoff;
                    if (!boostingBackward)
                        targetYaw = Mathf.Atan2(horizontalVel.x, horizontalVel.z) * Mathf.Rad2Deg;
                }
                break;
        }

        bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, targetYaw, turnSpeed * Time.deltaTime);

        // Placeholder ball spin (the real model will play a ball animation instead).
        bool spinning = Mode == ShadowMoveMode.SpinDash || Mode == ShadowMoveMode.BallDash;
        spinAngle = spinning ? (spinAngle + spinDegPerSec * Time.deltaTime) % 360f : 0f;
        visual.rotation = Quaternion.Euler(0f, bodyYaw, 0f) * Quaternion.Euler(spinAngle, 0f, 0f);

        ApplyVisualPose();
    }

    static float YawOf(Vector3 dir, float fallback)
        => (dir.x * dir.x + dir.z * dir.z) > 0.0001f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : fallback;

    // PLACEHOLDER body shapes for the capsule prototype. The real model replaces these with animations.
    //   Slide = squashed flat, spin dash / ball dash = small rolling ball.
    void ApplyVisualPose()
    {
        Vector3 scale = Vector3.one;
        float y = visualBasePos.y;
        switch (Mode)
        {
            case ShadowMoveMode.Slide:
                scale = new Vector3(1f, 0.55f, 1f);
                y = visualBasePos.y * 0.55f; // keep the squashed capsule sitting on the floor
                break;
            case ShadowMoveMode.SpinDash:
                scale = Vector3.one * 0.6f;
                y = visualBasePos.y * 0.6f;
                break;
            case ShadowMoveMode.BallDash:
                scale = Vector3.one * 0.6f;
                break;
        }
        visual.localScale = Vector3.Scale(visualBaseScale, scale);
        visual.localPosition = new Vector3(visualBasePos.x, y, visualBasePos.z);
    }

    // ---------------- Aim ----------------
    // AIM: the arm points at whatever is under the crosshair.
    // Called BY ShadowCameraRig at the end of ITS LateUpdate, so the camera has already
    // moved this frame. (Doing this in our own Update would lag the arm one frame behind.)
    public void UpdateAim()
    {
        Transform camT = rig.cam.transform;

        if (CrosshairCast(camT.forward, aimMaxDistance, out RaycastHit hit))
            AimPoint = hit.point;
        else
            AimPoint = camT.position + camT.forward * aimMaxDistance;

        // The arm sits beside Shadow, not at the camera, so point it at the AimPoint.
        Vector3 toAim = AimPoint - aimArm.position;
        aimArm.rotation = toAim.sqrMagnitude > minAimDistance * minAimDistance
            ? Quaternion.LookRotation(toAim)
            : camT.rotation; // too close: avoid wild swinging
    }

    // THE CROSSHAIR RAY, for aiming AND shooting. It starts at the CAMERA and returns the nearest thing
    // under the reticle, ignoring Shadow himself and anything clearly BEHIND him (between camera and Shadow).
    // Starting at the camera (not at the gun) is what makes point-blank shots reliable: a ray that starts
    // INSIDE an enemy's collider can't hit it, but the camera ray reaches the enemy's front surface first.
    // `dir` is normally the camera's forward; the weapon passes spread directions too.
    public bool CrosshairCast(Vector3 dir, float maxDist, out RaycastHit best)
    {
        Transform camT = rig.cam.transform;
        Vector3 chest = transform.position + Vector3.up;
        // Anything more than 0.75 m behind Shadow's depth (as seen from the camera) doesn't count.
        float minDepth = Vector3.Dot(chest - camT.position, camT.forward) - 0.75f;

        int n = Physics.RaycastNonAlloc(camT.position, dir, aimHits, maxDist, aimMask,
                                        QueryTriggerInteraction.Ignore);
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (aimHits[i].collider.transform.IsChildOf(transform)) continue;                   // Shadow himself
            if (Vector3.Dot(aimHits[i].point - camT.position, camT.forward) < minDepth) continue; // behind Shadow
            if (aimHits[i].distance < bestDist)
            {
                bestDist = aimHits[i].distance;
                best = aimHits[i];
                found = true;
            }
        }
        return found;
    }

    // A plain world raycast from any point that ignores Shadow's own colliders (used by the weapon's cover check).
    public bool WorldCast(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit hit)
        => RaycastIgnoringSelf(origin, dir, maxDist, out hit);

    // Nearest hit that is NOT part of Shadow himself (the Player object or any child).
    // This makes every query independent of layer setup: his own capsule, face cube or
    // arm can never block the crosshair, a wall probe or a lock-on check.
    bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        => RaycastIgnoringSelf(origin, dir, maxDist, aimMask, out best);

    bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, LayerMask mask, out RaycastHit best)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, aimHits, maxDist, mask,
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