using UnityEngine;

// ============================================================================
// ShadowMotor_Wall.cs                                                (v0.5)
// PART OF: ShadowMotor (a partial class). Do NOT attach this file to anything.
//
// LEDGE MANTLE  Airborne, pressing into a ledge that is within arm's reach:
//               Shadow pulls himself up and over automatically.
// WALL RUN      Airborne, fast enough, moving ALONG a wall (not head-on):
//               he sticks to it and runs for a limited time (timed = Desperate).
//               Pull back (S) or Ctrl (slam) to drop off. Jump = wall jump.
// WALL JUMP     Jump during a wall run, or next to any wall in the air
//               (a "wall kick"): pushes you away and up, keeps your speed along
//               the wall. Limited per airtime, refilled on the ground.
//
// LEVEL DESIGN NOTE: `wallRunMask` decides which layers can be wall-run. It is
// Everything for now. Later, make a "Runnable" layer so walls are chosen, not accidental.
// ============================================================================
public partial class ShadowMotor
{
    [Header("Ledge Mantle")]
    public float mantleReachHeight = 1.8f;   // how far above his feet Shadow can grab a ledge
    public float mantleMinRise = 0.3f;       // ledges lower than this are just walked onto
    public float mantleProbeDistance = 0.8f; // how far ahead he looks for a wall
    public float mantleDuration = 0.28f;     // seconds for the pull-up
    public float mantleExitSpeed = 5f;       // speed carried onto the ledge
    public float mantleCooldown = 0.35f;

    [Header("Wall Run")]
    public LayerMask wallRunMask = ~0;       // which layers can be wall-run (see note at the top)
    public float wallCheckDistance = 0.8f;   // how close a wall must be to grab it
    public float wallRunMinSpeed = 6f;       // flat speed needed to start
    public float wallRunSpeed = 15f;         // run speed (or your current speed if higher)
    public float wallRunDuration = 2.2f;     // seconds of wall run: the timer
    public float wallRunLift = 2f;           // little upward pop when grabbing the wall
    [Range(0f, 1f)]
    public float wallRunGravityScale = 0.12f;// fraction of normal gravity while running (slow sink)
    public float wallRunMaxFall = 3f;        // never sinks faster than this
    public float wallStick = 2f;             // gentle push into the wall so he stays on it
    public float wallRunSameWallCooldown = 0.5f; // can't immediately re-grab the wall you just left
    public int maxWallRunsPerAir = 3;        // wall runs per airtime (refilled on the ground)

    [Header("Wall Jump / Wall Kick")]
    public float wallJumpAwaySpeed = 9f;     // push away from the wall
    public float wallJumpHeight = 1.8f;      // height of the hop (metres)
    [Range(0f, 1f)]
    public float wallJumpAlongKeep = 0.85f;  // fraction of your speed ALONG the wall that is kept
    public int maxWallJumpsPerAir = 3;       // wall jumps per airtime (refilled on the ground)

    public bool IsWallRunning => Mode == ShadowMoveMode.WallRun;
    public bool IsMantling => Mode == ShadowMoveMode.Mantle;
    // +1 = wall on Shadow's right, -1 = on his left, 0 = not wall running. The camera reads this.
    public int WallSide => Mode == ShadowMoveMode.WallRun ? wallSide : 0;

    int wallRunsLeft, wallJumpsLeft, wallSide;
    Vector3 wallNormal, wallTangent;         // flat wall normal, and the direction we run along it
    Collider wallCollider, lastWallCollider;
    float wallRunTimer, wallRunCurrentSpeed, lastWallTime;

    Vector3 mantleStartPos, mantleTopPos, mantleInward;
    float mantleT, nextMantleTime;

    static readonly Vector3[] kickDirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    // =====================================================================
    // LEDGE MANTLE
    // =====================================================================
    bool TryStartMantle(Vector3 wishDir, bool hasInput)
    {
        if (Time.time < nextMantleTime) return false;

        // Which way are we pushing? Input if any, otherwise our travel direction.
        Vector3 probe = hasInput ? wishDir : new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        if (probe.sqrMagnitude < 0.25f) return false;
        probe.Normalize();

        Vector3 feet = transform.position;

        // 1. Is there a wall face right in front of us at waist height?
        if (!RaycastIgnoringSelf(feet + Vector3.up, probe, mantleProbeDistance, out RaycastHit wall)) return false;
        if (Mathf.Abs(wall.normal.y) > 0.3f) return false;                 // must be a vertical face
        Vector3 inward = -new Vector3(wall.normal.x, 0f, wall.normal.z).normalized;
        if (Vector3.Dot(probe, inward) < 0.4f) return false;               // must be moving INTO it

        // 2. Find the top of the ledge: drop a ray from just above our reach, slightly inside the wall.
        Vector3 downOrigin = wall.point + inward * 0.35f;
        downOrigin.y = feet.y + mantleReachHeight + 0.4f;
        if (!RaycastIgnoringSelf(downOrigin, Vector3.down, mantleReachHeight + 0.4f, out RaycastHit top)) return false;
        if (top.normal.y < 0.7f) return false;                             // must be a flat-ish top

        float rise = top.point.y - feet.y;
        if (rise < mantleMinRise || rise > mantleReachHeight) return false;

        // 3. Where we would stand, and is there room to stand there?
        Vector3 topPos = new Vector3(wall.point.x, top.point.y + 0.03f, wall.point.z) + inward * 0.6f;
        if (!CapsuleClearAt(topPos)) return false;

        mantleStartPos = feet;
        mantleTopPos = topPos;
        mantleInward = inward;
        mantleT = 0f;
        Mode = ShadowMoveMode.Mantle;
        horizontalVel = Vector3.zero;
        verticalVel = 0f;
        lastJumpPressTime = -10f;
        carryMomentum = false;
        return true;
    }

    // The pull-up path: straight up the wall face first, then over the edge.
    // We compute the position we SHOULD be at, and set the velocity that gets us there,
    // so the CharacterController still handles collisions.
    void MantleStep()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        mantleT = Mathf.Min(1f, mantleT + dt / mantleDuration);

        const float split = 0.6f; // first 60% of the time: climb. last 40%: move over the edge.
        Vector3 corner = new Vector3(mantleStartPos.x, mantleTopPos.y, mantleStartPos.z);
        Vector3 p = mantleT < split
            ? Vector3.Lerp(mantleStartPos, corner, Mathf.SmoothStep(0f, 1f, mantleT / split))
            : Vector3.Lerp(corner, mantleTopPos, Mathf.SmoothStep(0f, 1f, (mantleT - split) / (1f - split)));

        Vector3 v = (p - transform.position) / dt;
        horizontalVel = new Vector3(v.x, 0f, v.z);
        verticalVel = v.y;
    }

    void MantlePostMove()
    {
        if (mantleT < 1f) return;
        Mode = ShadowMoveMode.Normal;
        horizontalVel = mantleInward * mantleExitSpeed;
        verticalVel = 0f;
        nextMantleTime = Time.time + mantleCooldown;
    }

    // =====================================================================
    // WALL RUN
    // =====================================================================
    bool TryStartWallRun(bool hasInput)
    {
        if (wallRunsLeft <= 0 || !hasInput) return false;
        if (Time.time - lastTouchGroundTime < 0.15f) return false; // not in the first instant of a jump

        Vector3 hv = new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        float speed = hv.magnitude;
        if (speed < wallRunMinSpeed) return false;

        Vector3 velDir = hv / speed;
        Vector3 chest = transform.position + Vector3.up;
        Vector3 right = Vector3.Cross(Vector3.up, velDir); // to the right of our travel direction

        for (int side = -1; side <= 1; side += 2)
        {
            if (!RaycastIgnoringSelf(chest, right * side, wallCheckDistance, wallRunMask, out RaycastHit hit)) continue;
            if (Mathf.Abs(hit.normal.y) > 0.25f) continue;                 // must be a vertical wall

            Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            if (Mathf.Abs(Vector3.Dot(velDir, n)) > 0.6f) continue;        // heading straight at it: not a run
            if (hit.collider == lastWallCollider && Time.time - lastWallTime < wallRunSameWallCooldown) continue;

            wallNormal = n;
            wallCollider = hit.collider;
            wallSide = side;
            wallTangent = Vector3.Cross(n, Vector3.up);
            if (Vector3.Dot(wallTangent, velDir) < 0f) wallTangent = -wallTangent; // run the way we were going

            wallRunCurrentSpeed = Mathf.Max(speed, wallRunSpeed);
            wallRunTimer = wallRunDuration;
            wallRunsLeft--;
            verticalVel = wallRunLift;
            carryMomentum = false;
            Mode = ShadowMoveMode.WallRun;
            return true;
        }
        return false;
    }

    // Runs every frame while Mode is WallRun. Sets horizontalVel and verticalVel.
    void WallRunStep(Vector3 wishDir, bool hasInput)
    {
        wallRunTimer -= Time.deltaTime;

        // Is the wall still there beside us?
        Vector3 chest = transform.position + Vector3.up;
        bool stillWall = RaycastIgnoringSelf(chest, -wallNormal, wallCheckDistance + 0.3f, wallRunMask, out RaycastHit hit)
                         && Mathf.Abs(hit.normal.y) <= 0.25f;
        bool pullOff = MoveInput.y < -0.5f; // pulling back (S) drops you off the wall

        if (!stillWall || wallRunTimer <= 0f || pullOff)
        {
            EndWallRun(wallRunTimer <= 0f || pullOff);
            NormalHorizontal(wishDir, hasInput); // hand control straight back this frame
            return;
        }

        // Follow the wall (it may curve), keeping the same running direction.
        wallNormal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
        wallCollider = hit.collider;
        Vector3 t = Vector3.Cross(wallNormal, Vector3.up);
        wallTangent = Vector3.Dot(t, wallTangent) < 0f ? -t : t;

        horizontalVel = wallTangent * wallRunCurrentSpeed - wallNormal * wallStick;
        verticalVel = Mathf.Max(verticalVel + gravity * wallRunGravityScale * Time.deltaTime, -wallRunMaxFall);
    }

    // Leaves the wall run. pushOff = a small shove away from the wall (timer ran out / pulled off).
    void EndWallRun(bool pushOff)
    {
        horizontalVel = wallTangent * wallRunCurrentSpeed;
        if (pushOff) horizontalVel += wallNormal * 2f;
        lastWallCollider = wallCollider;
        lastWallTime = Time.time;
        Mode = ShadowMoveMode.Normal;
        carryMomentum = true;
    }

    // =====================================================================
    // WALL JUMP / WALL KICK
    // =====================================================================
    // Looks for a wall within reach in the four directions around Shadow (for a wall kick).
    bool TryFindWallForKick(out RaycastHit best)
    {
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;

        Vector3 chest = transform.position + Vector3.up;
        Quaternion yaw = Quaternion.Euler(0f, rig.Yaw, 0f);
        for (int i = 0; i < kickDirs.Length; i++)
        {
            if (!RaycastIgnoringSelf(chest, yaw * kickDirs[i], wallCheckDistance, wallRunMask, out RaycastHit hit)) continue;
            if (Mathf.Abs(hit.normal.y) > 0.25f) continue;
            if (hit.distance < bestDist) { bestDist = hit.distance; best = hit; found = true; }
        }
        return found;
    }

    // Pushes away from the wall and up, keeping most of the speed ALONG the wall.
    // Mid wall run it always works; a wall kick off a standing wall uses up one of wallJumpsLeft.
    void DoWallJump(Vector3 normal, Collider wall)
    {
        Vector3 n = new Vector3(normal.x, 0f, normal.z).normalized;
        Vector3 hv = new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        Vector3 along = hv - n * Vector3.Dot(hv, n); // drop the part of our speed that points into/out of the wall

        horizontalVel = n * wallJumpAwaySpeed + along * wallJumpAlongKeep;
        verticalVel = Mathf.Sqrt(2f * -gravity * wallJumpHeight);

        wallJumpsLeft = Mathf.Max(0, wallJumpsLeft - 1);
        lastWallCollider = wall;
        lastWallTime = Time.time;

        Mode = ShadowMoveMode.Normal;
        carryMomentum = true;
        lastJumpPressTime = -10f;
    }
}