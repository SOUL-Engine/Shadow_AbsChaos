using UnityEngine;

// ============================================================================
// ShadowCameraRig.cs                                                 (v0.4)
// GOES ON: CameraRig (an empty object in the scene root, NOT a child of Player)
//
// HIERARCHY THIS SCRIPT EXPECTS:
//   CameraRig              <- THIS script lives here. It orbits the player (yaw + pitch).
//    └ Main Camera        <- drag into `cam`. Its position AND rotation are set by this
//                            script every frame, so never move it by hand while playing.
//
// JOB: Owns the camera: mouse look, AC6-style framing, follow lag, fall/landing
// framing, strafe roll, collision, FOV.
//
// FRAMING RULES:
//   * Camera sits ABOVE and behind Shadow, so moving forward he is BELOW the crosshair.
//   * Strafe direction picks the screen side (Shadow on screen-RIGHT: NW, W, SE;
//     screen-LEFT: NE, E, SW; centred: N, S, idle). The CAMERA slides to the opposite side.
//   * The camera rolls a few degrees toward the strafe direction.
//   * The camera trails Shadow (follow lag). During a Chaos dodge the lag limit is raised
//     so the camera whips after him instead of snapping.
//   * Jumping lifts the camera. Falling drops it BELOW Shadow. Landing dips, then springs back.
//   * The camera never clips through walls (SphereCast from the pivot), and it
//     IGNORES Shadow's own colliders (see the collision section for why).
// ============================================================================
public class ShadowCameraRig : MonoBehaviour
{
    // -------------------- Inspector: references --------------------
    [Header("References (drag these in)")]
    public Transform target;     // Player (the root object)
    public ShadowMotor motor;    // the ShadowMotor on Player
    public Camera cam;           // CameraRig/Main Camera

    // -------------------- Inspector: look --------------------
    [Header("Look")]
    public float sensitivity = 0.12f;
    public float minPitch = -35f;   // NEGATIVE = looking UP. This is the look-up limit.
    public float maxPitch = 60f;    // POSITIVE = looking DOWN. This is the look-down limit.

    // -------------------- Inspector: framing --------------------
    [Header("Framing")]
    public Vector3 pivotOffset = new Vector3(0f, 1.5f, 0f); // orbit point, roughly Shadow's chest
    public float backDistance = 4.5f;   // how far behind the pivot the camera sits
    public float baseHeight = 0.9f;     // camera height above the pivot (puts Shadow below the crosshair)
    public float jumpRise = 0.6f;       // extra camera height while rising in a jump
    public float sideOffset = 2.4f;     // how far the camera slides sideways

    [Header("Screen Side Rules")]
    // +1 = Shadow on screen-RIGHT, -1 = Shadow on screen-LEFT, 0 = centred.
    // Order: N, NE, E, SE, S, SW, W, NW (the 8 keyboard directions, relative to the camera).
    public float[] sideBySector = { 0f, -1f, -1f, +1f, 0f, -1f, +1f, +1f };
    public float sideCommitDelay = 0.08f; // a new side must be held this long before swapping
    public float sideSmoothing = 10f;     // higher = camera slides across faster

    [Header("Strafe Roll")]
    public float maxRoll = 3.5f;     // degrees of tilt at full sideways speed. NEGATE if it leans the wrong way.
    public float rollSmoothing = 8f;
    public float wallRunRoll = 9f;   // extra tilt AWAY from the wall while wall running (Titanfall-style)

    [Header("Slide")]
    public float slideCameraDrop = 0.5f; // camera lowers this much while sliding / spin dashing
    public float slidePullIn = 1.0f;     // camera moves this much CLOSER while sliding / spin dashing
    public float slideFovAdd = 14f;      // FOV widening while sliding / spin dashing (like boosting)
    public float slideZoomRate = 8f;     // how fast the pull-in eases in and out

    [Header("Follow Lag")]
    public float horizontalFollowRate = 10f; // LOWER = more lag. Lag distance is about speed / rate
    public float maxFollowLag = 2f;          // hard cap (metres) so Shadow can never outrun the camera
    public float warpExtraLag = 2.5f;        // extra cap while Chaos-dodging: the camera whips after him

    [Header("Homing Chain Camera (Sonic Generations-style hold)")]
    public float homingHoldDuration = 1.4f;  // seconds the camera stays put after a homing hit (back off, shoot...)
    public float homingHoldFollowRate = 5f;  // how fast it drifts to its held spot (e.g. to the PREVIOUS hit on a 2nd homing attack)
    public float releaseFollowRate = 3f;     // follow speed right after the hold ends; ramps back to normal
    public float holdMaxLag = 8f;            // Shadow is never allowed further than this (metres) from the held camera
    public float holdMaxLagVertical = 5f;

    [Header("Falling (camera drops below Shadow)")]
    public float fallHeightCentre = 0.25f; // camera height over the pivot while falling, Shadow CENTRED
    public float fallHeightSide = -0.6f;   // ...while Shadow is at a SIDE (negative = Shadow above crosshair)
    public float minHeight = -1.0f;        // the camera never goes lower than this relative to the pivot
    public float jumpRiseRate = 8f;        // how fast the camera lifts on a jump
    public float fallDownRate = 9f;        // how fast the camera drops when falling

    [Header("Landing Dip")]
    public float landDipMin = 0.15f;       // dip for a light landing
    public float landDipMax = 0.6f;        // dip for a heavy landing / slam
    public float landImpactForMax = 30f;   // impact speed (m/s) that gives the max dip
    public float landDipHold = 0.07f;      // seconds the camera holds the dip
    public float landDipRate = 30f;        // how fast it drops INTO the dip
    public float landRecoverRate = 16f;    // how fast it springs back up

    [Header("Collision")]
    public LayerMask collisionMask = ~0;   // which layers block the camera. Shadow's own colliders
                                           // are ignored in code, so Everything is fine.
    public float cameraRadius = 0.25f;     // size of the "ball" swept from the pivot to the camera
    public float collisionPadding = 0.05f;
    public float minCameraDistance = 1.0f; // never pulled closer than this to the pivot
    public float collisionRecover = 5f;    // how fast the camera eases back out after a wall

    [Header("FOV (Unity's fieldOfView is VERTICAL: 60 is roughly 91 degrees horizontal at 16:9)")]
    public float baseFov = 60f;
    public float boostFovAdd = 18f;   // widens when boosting forward
    public float backFovSub = 6f;     // narrows slightly when moving backward
    public float slamFovAdd = 10f;    // extra widening during a ground slam
    public float dodgeFovKick = 14f;  // instant FOV punch at the start of a Chaos dodge
    public float fovKickDecay = 9f;   // how fast the punch settles
    public float fovSmoothing = 8f;

    // -------------------- Read by other scripts --------------------
    public float Yaw { get; private set; }   // degrees, horizontal look
    public float Pitch { get; private set; } // degrees, vertical look

    // -------------------- Private state --------------------
    ShadowInput input;
    float committedSide, candidateSide, candidateSince; // screen-side hysteresis
    float currentX, currentY, currentRoll;              // smoothed camera offsets
    float landTimer, landY;                             // landing dip state
    Vector3 smoothedPivot;                              // the lagging orbit point
    float lagCap;                                       // current follow-lag limit
    float collisionDist;                                // current camera distance after collision
    float fovCurrent, fovKick;                          // smoothed FOV + the instant punch on top
    readonly RaycastHit[] camHits = new RaycastHit[16];
    // Homing-chain hold state
    bool holdActive, hasLastHit;
    float holdTimer, currentFollowRate, currentBack;
    Vector3 holdPivot, lastHitPivot;

    void Start()
    {
        input = motor.GetComponent<ShadowInput>();
        Cursor.lockState = CursorLockMode.Locked; // Esc frees the cursor in the editor
        currentY = baseHeight;
        collisionDist = backDistance;
        lagCap = maxFollowLag;
        currentFollowRate = horizontalFollowRate;
        currentBack = backDistance;
        smoothedPivot = target.position + pivotOffset;
        fovCurrent = baseFov;
        cam.fieldOfView = baseFov;
    }

    // LateUpdate runs AFTER every Update, so the player has already moved this frame.
    void LateUpdate()
    {
        // ---- 1. Mouse look ----
        Vector2 look = input.Look;                 // already per-frame: do NOT multiply by deltaTime
        Yaw += look.x * sensitivity;
        Pitch = Mathf.Clamp(Pitch - look.y * sensitivity, minPitch, maxPitch); // mouse up = Pitch goes negative

        // ---- 2. Events from the motor ----
        if (motor.LandedThisFrame)
        {
            // Start a dip BELOW wherever the camera is right now.
            float t = Mathf.Clamp01(motor.LastImpactSpeed / landImpactForMax);
            float dip = Mathf.Lerp(landDipMin, landDipMax, t);
            landY = Mathf.Max(currentY - dip, minHeight);
            landTimer = landDipHold;
        }
        if (motor.DodgeStartedThisFrame || motor.BallDashStartedThisFrame) fovKick = dodgeFovKick;

        // ---- 3. Screen side (sideways offset) ----
        UpdateSide(motor.MoveInput);
        // While wall running, keep Shadow on the WALL side of the screen so the camera sits clear of the wall.
        if (motor.IsWallRunning) committedSide = motor.WallSide;
        float targetX = -committedSide * sideOffset; // camera goes to the OPPOSITE side of where Shadow should appear
        currentX = Smooth(currentX, targetX, sideSmoothing);

        // ---- 4. Camera height: standing / jumping / falling / landing ----
        float sideAmount = Mathf.Clamp01(Mathf.Abs(currentX) / Mathf.Max(0.01f, sideOffset)); // 0 centred .. 1 fully to a side
        bool falling = motor.IsAirborne && motor.Velocity.y < -0.5f;
        float targetY, yRate;
        if (landTimer > 0f)
        {
            targetY = landY;                         // hold the dip
            yRate = landDipRate;
            landTimer -= Time.deltaTime;
        }
        else if (!motor.IsAirborne)
        {
            targetY = baseHeight - ((motor.IsSliding || motor.IsSpinDashing) ? slideCameraDrop : 0f);                    // standing: spring back up quickly after a landing
            yRate = landRecoverRate;
        }
        else if (falling)
        {
            targetY = Mathf.Lerp(fallHeightCentre, fallHeightSide, sideAmount); // drop below Shadow
            yRate = fallDownRate;
        }
        else
        {
            targetY = baseHeight + jumpRise;         // rising
            yRate = jumpRiseRate;
        }
        currentY = Smooth(currentY, targetY, yRate);

        // ---- 5. Follow lag, plus the homing-chain HOLD ----
        Vector3 desiredPivot = target.position + pivotOffset;

        // Sonic Generations-style chain camera:
        //  * A homing HIT freezes the camera where it is, so you can back off and shoot without it lunging after you.
        //  * The NEXT homing attack sends the camera to where the previous hit happened, then it holds again.
        //  (Mouse look is never frozen: only the camera's POSITION is.)
        if (motor.HomingStartedThisFrame && holdActive && hasLastHit)
        {
            holdPivot = lastHitPivot;
            holdTimer = Mathf.Max(holdTimer, motor.homingMaxTime + 0.2f); // don't release mid-flight
        }
        if (motor.HomingHitThisFrame)
        {
            lastHitPivot = desiredPivot;
            hasLastHit = true;
            holdPivot = smoothedPivot;       // freeze right where the camera is now
            holdActive = true;
            holdTimer = homingHoldDuration;
        }
        if (holdActive)
        {
            holdTimer -= Time.deltaTime;
            if (holdTimer <= 0f) { holdActive = false; currentFollowRate = releaseFollowRate; }
        }

        Vector3 pivotGoal = holdActive ? holdPivot : desiredPivot;
        currentFollowRate = holdActive ? homingHoldFollowRate : Smooth(currentFollowRate, horizontalFollowRate, 3f);
        bool easing = holdActive || currentFollowRate < horizontalFollowRate - 0.5f; // holding, or still catching up

        float follow = 1f - Mathf.Exp(-currentFollowRate * Time.deltaTime);
        smoothedPivot.x = Mathf.Lerp(smoothedPivot.x, pivotGoal.x, follow);
        smoothedPivot.z = Mathf.Lerp(smoothedPivot.z, pivotGoal.z, follow);
        // Vertical is rigid normally (jump/fall feel comes from currentY above); while holding or catching up it eases too.
        smoothedPivot.y = easing ? Mathf.Lerp(smoothedPivot.y, pivotGoal.y, follow) : desiredPivot.y;

        // The lag limit jumps UP instantly during a warp or a hold, then eases back down afterwards.
        float capTarget = holdActive ? holdMaxLag
                        : maxFollowLag + ((motor.IsDodging || motor.IsBallDashing) ? warpExtraLag : 0f);
        lagCap = capTarget > lagCap ? capTarget : Smooth(lagCap, capTarget, 3f);

        Vector3 lag = desiredPivot - smoothedPivot; lag.y = 0f;
        if (lag.magnitude > lagCap)
        {
            Vector3 clamped = desiredPivot - lag.normalized * lagCap;
            smoothedPivot.x = clamped.x;
            smoothedPivot.z = clamped.z;
        }
        if (holdActive)
            smoothedPivot.y = Mathf.Clamp(smoothedPivot.y, desiredPivot.y - holdMaxLagVertical, desiredPivot.y + holdMaxLagVertical);

        // ---- 6. Place the rig (turns with the mouse) ----
        transform.position = smoothedPivot;
        transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);

        // ---- 7. Place the camera, with wall collision ----
        float backGoal = backDistance - ((motor.IsSliding || motor.IsSpinDashing) ? slidePullIn : 0f);
        currentBack = Smooth(currentBack, backGoal, slideZoomRate);
        Vector3 desiredCam = transform.TransformPoint(new Vector3(currentX, currentY, -currentBack));
        Vector3 toCam = desiredCam - smoothedPivot;
        float dist = toCam.magnitude;
        Vector3 dir = toCam / Mathf.Max(dist, 0.0001f);

        float allowed = dist;
        if (SphereCastIgnoringSelf(smoothedPivot, dir, dist + collisionPadding, out float hitDist))
            allowed = Mathf.Max(minCameraDistance, hitDist - collisionPadding);

        // Snap IN instantly (never show the inside of a wall); ease back OUT smoothly.
        collisionDist = allowed < collisionDist
            ? allowed
            : Mathf.Lerp(collisionDist, allowed, 1f - Mathf.Exp(-collisionRecover * Time.deltaTime));
        collisionDist = Mathf.Min(collisionDist, dist);

        cam.transform.position = smoothedPivot + dir * collisionDist;

        // ---- 8. Strafe roll: lean toward the direction of sideways travel ----
        Vector3 hv = motor.Velocity; hv.y = 0f;
        Vector3 camRight = Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
        float lateral = Mathf.Clamp(Vector3.Dot(hv, camRight) / motor.boostSpeed, -1f, 1f);
        float wallRoll = motor.IsWallRunning ? motor.WallSide * wallRunRoll : 0f; // +Z = lean left, so a wall on the right leans us left (away)
        currentRoll = Smooth(currentRoll, -lateral * maxRoll + wallRoll, rollSmoothing); // Unity: +Z roll = lean left
        cam.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, currentRoll);

        UpdateFov();

        // ---- 9. Camera is final for this frame: point the arm at the crosshair ----
        motor.UpdateAim();
    }

    // Sweeps a ball from the pivot toward the camera and reports the nearest wall.
    //
    // WHY WE FILTER: the pivot lags behind Shadow, so when you move sideways or backward his
    // own body sits right on the path between pivot and camera. A plain SphereCast would hit
    // HIM, slam the camera into first person, then ease back out (the v0.3 glitch).
    // So anything under the Player object is skipped, whatever layer it is on.
    // Hits at distance 0 (the ball starting inside something) are skipped too, so a pivot
    // brushing a corner can't pop the camera to the minimum distance.
    bool SphereCastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out float hitDist)
    {
        int n = Physics.SphereCastNonAlloc(origin, cameraRadius, dir, camHits, maxDist,
                                           collisionMask, QueryTriggerInteraction.Ignore);
        hitDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (camHits[i].collider.transform.IsChildOf(target)) continue; // Shadow himself
            if (camHits[i].distance <= 0f) continue;                       // started inside: ignore
            if (camHits[i].distance < hitDist)
            {
                hitDist = camHits[i].distance;
                found = true;
            }
        }
        return found;
    }

    // Turns the current WASD direction into a committed screen side.
    void UpdateSide(Vector2 move)
    {
        float desired = GetSideForDirection(move);

        // Hysteresis: only commit after the request has been stable for sideCommitDelay.
        if (!Mathf.Approximately(desired, candidateSide))
        {
            candidateSide = desired;
            candidateSince = Time.time;
        }
        if (Time.time - candidateSince >= sideCommitDelay)
            committedSide = candidateSide;
    }

    float GetSideForDirection(Vector2 move)
    {
        if (move.sqrMagnitude < 0.01f) return 0f; // standing still: centred

        // Angle clockwise from forward: N=0, E=90, S=180, W=270.
        float angle = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;

        int sector = Mathf.RoundToInt(angle / 45f) % 8; // 0..7 = N,NE,E,SE,S,SW,W,NW
        return sideBySector[sector];
    }

    void UpdateFov()
    {
        // How fast are we moving along the camera's forward axis? (+ toward, - away)
        Vector3 camFwd = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        Vector3 hv = motor.Velocity; hv.y = 0f;
        float along = Vector3.Dot(hv, camFwd);

        float fovOffset = 0f;
        if (along > motor.walkSpeed)       // faster than walking, toward forward = boosting/warping forward
            fovOffset = boostFovAdd * Mathf.InverseLerp(motor.walkSpeed, motor.boostSpeed, along);
        else if (along < 0f)               // moving backward
            fovOffset = -backFovSub * Mathf.Clamp01(-along / motor.walkSpeed);

        if (motor.IsSlamming) fovOffset += slamFovAdd;
        if (motor.IsSliding || motor.IsSpinDashing) fovOffset = Mathf.Max(fovOffset, slideFovAdd); // like boosting, whichever is bigger

        // Smoothed FOV, plus the dodge punch added on top (so the punch isn't smoothed away).
        fovCurrent = Smooth(fovCurrent, baseFov + fovOffset, fovSmoothing);
        fovKick = Smooth(fovKick, 0f, fovKickDecay);
        cam.fieldOfView = fovCurrent + fovKick;
    }

    // Frame-rate independent smoothing (same feel at 60 or 240 fps).
    static float Smooth(float current, float target, float rate)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * Time.deltaTime));
}