using UnityEngine;

// ============================================================================
// ShadowCameraRig.cs
// GOES ON: CameraRig (an empty object in the scene root, NOT a child of Player)
//
// HIERARCHY THIS SCRIPT EXPECTS:
//   CameraRig              <- THIS script lives here. It orbits the player (yaw + pitch).
//    └ Main Camera        <- drag into `cam`. Its local position is set by this script
//                            every frame, so never move it by hand while playing.
//
// JOB: Owns the camera. Mouse look, the AC6-style framing rules, and FOV.
//
// FRAMING RULES (all in rig-local space, see LateUpdate):
//   * Camera sits ABOVE and behind Shadow, so with pure forward movement he
//     appears BELOW the crosshair (Armored Core style).
//   * Strafe direction decides which side of the screen Shadow appears on:
//       Shadow on screen-RIGHT : NW, W, SE
//       Shadow on screen-LEFT  : NE, E, SW
//       Shadow centred         : N, S, and standing still
//     (Moving the CAMERA to the opposite side is what shifts him on screen.)
//   * Jumping / falling lifts the camera a little.
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
    public float baseHeight = 0.9f;     // camera height above the pivot. THIS is what puts
                                        // Shadow below the crosshair. Raise = Shadow lower on screen.
    public float jumpRise = 0.6f;       // extra camera height while airborne ("a little bit")
    public float sideOffset = 1.1f;     // how far the camera slides sideways when swapping sides

    [Header("Screen Side Rules")]
    // +1 = Shadow on screen-RIGHT, -1 = Shadow on screen-LEFT, 0 = centred.
    // Order: N, NE, E, SE, S, SW, W, NW  (the 8 keyboard directions, relative to the camera).
    // Edit these in the Inspector to tune; no code change needed.
    public float[] sideBySector = { 0f, -1f, -1f, +1f, 0f, -1f, +1f, +1f };
    public float sideCommitDelay = 0.08f; // a new side must be held this long before swapping
                                          // (stops flicker when two keys aren't pressed on the same frame)
    public float sideSmoothing = 10f;     // higher = camera slides across faster
    public float heightSmoothing = 8f;

    // -------------------- Inspector: FOV --------------------
    [Header("FOV (Unity's fieldOfView is VERTICAL: 60 is roughly 91 degrees horizontal at 16:9)")]
    public float baseFov = 60f;
    public float boostFovAdd = 18f;   // widens when boosting forward
    public float backFovSub = 6f;     // narrows slightly when moving backward
    public float fovSmoothing = 8f;

    // -------------------- Read by other scripts --------------------
    public float Yaw { get; private set; }   // degrees, horizontal look
    public float Pitch { get; private set; } // degrees, vertical look

    // -------------------- Private state --------------------
    ShadowInput input;
    float committedSide;   // the side we have actually decided on (-1, 0, +1)
    float candidateSide;   // the side the keys are asking for right now
    float candidateSince;  // when that request started
    float currentX;        // smoothed camera sideways offset
    float currentY;        // smoothed camera height offset

    void Start()
    {
        input = motor.GetComponent<ShadowInput>();
        Cursor.lockState = CursorLockMode.Locked; // Esc frees the cursor in the editor
        currentY = baseHeight;
        cam.fieldOfView = baseFov;
    }

    // LateUpdate runs AFTER every Update, so the player has already moved this frame.
    void LateUpdate()
    {
        // ---- 1. Mouse look ----
        Vector2 look = input.Look;                 // already per-frame: do NOT multiply by deltaTime
        Yaw += look.x * sensitivity;
        Pitch = Mathf.Clamp(Pitch - look.y * sensitivity, minPitch, maxPitch); // mouse up = Pitch goes negative

        // ---- 2. Decide framing targets ----
        UpdateSide(motor.MoveInput);
        // Camera goes to the OPPOSITE side of where Shadow should appear on screen.
        float targetX = -committedSide * sideOffset;
        float targetY = baseHeight + (motor.IsAirborne ? jumpRise : 0f);
        currentX = Smooth(currentX, targetX, sideSmoothing);
        currentY = Smooth(currentY, targetY, heightSmoothing);

        // ---- 3. Place the rig and camera ----
        transform.position = target.position + pivotOffset;        // rig sits on Shadow's chest
        transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);     // rig turns with the mouse
        cam.transform.localPosition = new Vector3(currentX, currentY, -backDistance); // x=side, y=height, z=behind
        cam.transform.localRotation = Quaternion.identity;         // camera looks exactly where the rig looks

        UpdateFov();

        // ---- 4. Now the camera is final for this frame: point the arm at the crosshair ----
        motor.UpdateAim();
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

        int sector = Mathf.RoundToInt(angle / 45f) % 8; // snap to 0..7 = N,NE,E,SE,S,SW,W,NW
        return sideBySector[sector];
    }

    void UpdateFov()
    {
        // How fast are we moving along the camera's forward axis? (+ toward, - away)
        Vector3 camFwd = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        Vector3 hv = motor.Velocity; hv.y = 0f;
        float along = Vector3.Dot(hv, camFwd);

        float fovOffset = 0f;
        if (along > motor.walkSpeed)       // faster than walking, toward the camera's forward = boosting forward
            fovOffset = boostFovAdd * Mathf.InverseLerp(motor.walkSpeed, motor.boostSpeed, along);
        else if (along < 0f)               // moving backward
            fovOffset = -backFovSub * Mathf.Clamp01(-along / motor.walkSpeed);

        cam.fieldOfView = Smooth(cam.fieldOfView, baseFov + fovOffset, fovSmoothing);
    }

    // Frame-rate independent smoothing (same feel at 60 or 240 fps).
    static float Smooth(float current, float target, float rate)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * Time.deltaTime));
}