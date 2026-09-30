using UnityEngine;

// ============================================================================
// ShadowMotor.cs
// GOES ON: Player (root object; needs CharacterController + ShadowInput too)
//
// JOB: Owns everything about how Shadow MOVES. Three independent things:
//   1. VELOCITY   - where he actually travels (WASD relative to the camera)
//   2. BODY FACING - which way the Visual child points (see UpdateFacing)
//   3. AIM         - where the arm points: always the camera crosshair target
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
    public float airborneGrace = 0.12f;// how long off the ground before IsAirborne turns true
                                       // (stops the camera twitching on bumpy ground)

    [Header("Boost Gauge (boost is NOT free)")]
    public float boostMax = 100f;
    public float boostDrainPerSec = 35f;   // ~2.9 s of continuous boost from full
    public float boostRegenPerSec = 20f;   // ~5 s to refill
    public float boostRegenDelay = 0.75f;  // pause after boosting before refill begins
    [Range(0f, 1f)]
    public float boostUnlockFraction = 0.3f; // after hitting 0 you are LOCKED OUT until the
                                             // gauge refills to this fraction (punishes spam)

    [Header("Facing")]
    public float turnSpeed = 900f;          // degrees/second the body can rotate
    [Range(-1f, 1f)]
    public float boostBackwardCutoff = -0.3f; // boosting backwards beyond this keeps facing the
                                              // camera instead of turning his back on the enemy

    [Header("Aim")]
    public LayerMask aimMask = ~0;    // in the Inspector: Everything EXCEPT the Player layer
    public float aimMaxDistance = 200f;
    public float minAimDistance = 3f; // closer than this the arm just points parallel to the camera

    // -------------------- Read by other scripts --------------------
    public Vector3 Velocity => new Vector3(horizontalVel.x, verticalVel, horizontalVel.z);
    public Vector2 MoveInput { get; private set; }   // raw WASD: x = right, y = forward
    public bool IsBoosting { get; private set; }
    public bool IsAirborne => Time.time - lastGroundedTime > airborneGrace;
    public float BoostNormalized => boost / boostMax; // 0..1, for the HUD
    public Vector3 AimPoint { get; private set; }     // world point under the crosshair

    // -------------------- Private state --------------------
    CharacterController cc;
    ShadowInput input;

    Vector3 horizontalVel;     // X/Z velocity, handled with acceleration
    float verticalVel;         // Y velocity, handled with gravity
    float lastGroundedTime = -10f;
    float lastJumpPressTime = -10f;

    float boost;               // current gauge value
    float lastBoostTime = -10f;
    bool boostLocked;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        input = GetComponent<ShadowInput>();
        boost = boostMax;
    }

    // Movement runs in Update. The camera follows in LateUpdate (after us), so no jitter.
    void Update()
    {
        // ---- 1. Read input ----
        MoveInput = input.Move;
        bool hasInput = MoveInput.sqrMagnitude > 0.01f;
        if (input.JumpPressed) lastJumpPressTime = Time.time;

        // ---- 2. Camera-relative wish direction ----
        // Only the camera's YAW matters. Looking up/down must never slow you down.
        Quaternion camYaw = Quaternion.Euler(0f, rig.Yaw, 0f);
        Vector3 wishDir = camYaw * new Vector3(MoveInput.x, 0f, MoveInput.y);

        // ---- 3. Boost gauge decides whether we are boosting this frame ----
        UpdateBoost(hasInput);

        // ---- 4. Horizontal movement: accelerate toward the target velocity ----
        float targetSpeed = IsBoosting ? boostSpeed : walkSpeed;
        Vector3 targetVel = wishDir * targetSpeed;
        float accel = cc.isGrounded ? (hasInput ? groundAccel : groundDecel) : airAccel;
        horizontalVel = Vector3.MoveTowards(horizontalVel, targetVel, accel * Time.deltaTime);

        // ---- 5. Vertical: gravity, coyote time, jump buffer ----
        if (cc.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (verticalVel < 0f) verticalVel = -2f; // small push keeps us glued to slopes
        }

        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressTime <= jumpBuffer;
        if (canJump && wantsJump)
        {
            verticalVel = Mathf.Sqrt(2f * -gravity * jumpHeight); // v = sqrt(2gh): reach exactly jumpHeight
            lastJumpPressTime = -10f;   // consume the buffered press
            lastGroundedTime = -10f;    // consume coyote time (no double jump)
        }
        verticalVel += gravity * Time.deltaTime;

        // ---- 6. Move the CharacterController ----
        cc.Move(Velocity * Time.deltaTime);

        // ---- 7. Rotate the body ----
        UpdateFacing();
    }

    // Boost rules: costs gauge while held, refills after a delay, locks out if you empty it.
    void UpdateBoost(bool hasInput)
    {
        // Lockout ends once the gauge has refilled enough.
        if (boostLocked && boost >= boostMax * boostUnlockFraction) boostLocked = false;

        IsBoosting = input.BoostHeld && hasInput && !boostLocked && boost > 0f;

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
    //   Strafing / standing / skating backward -> body faces the CAMERA yaw (combat default)
    //   Boosting (not backward)                -> body faces the VELOCITY (Sonic forward lean)
    // Skating backward = legs travel backward while the body still faces the enemy.
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
    // moved this frame. (If we did this in our own Update the arm would lag one frame behind.)
    public void UpdateAim()
    {
        Transform camT = rig.cam.transform;

        // Ray from the camera through the crosshair (screen centre = camera forward).
        if (Physics.Raycast(camT.position, camT.forward, out RaycastHit hit,
                            aimMaxDistance, aimMask, QueryTriggerInteraction.Ignore))
            AimPoint = hit.point;
        else
            AimPoint = camT.position + camT.forward * aimMaxDistance;

        // The arm sits beside Shadow, not at the camera, so point it at the AimPoint.
        // That makes the arm and crosshair converge instead of pointing in parallel.
        Vector3 toAim = AimPoint - aimArm.position;
        aimArm.rotation = toAim.sqrMagnitude > minAimDistance * minAimDistance
            ? Quaternion.LookRotation(toAim)
            : camT.rotation; // too close: avoid wild swinging
    }

    // TEMPORARY boost bar so you can feel the gauge before a real HUD exists.
    void OnGUI()
    {
        Rect bg = new Rect(20, Screen.height - 40, 240, 16);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(bg, Texture2D.whiteTexture);
        GUI.color = boostLocked ? Color.red : Color.cyan; // red = locked out
        GUI.DrawTexture(new Rect(bg.x + 2, bg.y + 2, (bg.width - 4) * BoostNormalized, bg.height - 4),
                        Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}