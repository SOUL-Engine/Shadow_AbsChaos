using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// ShadowMotor_AirDash.cs                                             (v0.6)
// PART OF: ShadowMotor (a partial class). Do NOT attach this file to anything.
//
// Press JUMP AGAIN while in the air and Shadow curls into his ball form:
//   * An enemy is locked on (HUD red box) -> HOMING ATTACK: curves into it, damages
//     it, then BOUNCES UP a little before falling again.
//   * No target -> BALL DASH: a short chaos-warp style shot along the crosshair
//     direction. It is a MOVEMENT technique, NOT an attack: it deals no damage and
//     enemies killed near it drop nothing. It COSTS STAMINA.
// Ball forms cannot shoot (see ShadowMotor.CanShoot).
//
// STAMINA IS THE BOOST GAUGE (one resource): boosting, spin dash, slam and the ball
// dash all spend it. Warps are separate (they have their own charges).
//   * The ball dash costs `airDashStaminaCost`. Homing is free by default, so you can
//     never be stuck at 0 in mid-air with no way to earn it back.
//   * Homing HITS give stamina back, scaled by the CHAIN: the 1st hit in a chain gives
//     nothing, the 2nd a little, ... the 5th gives two thirds of a full gauge
//     (see `homingRegainByChain`). Hitting the SAME enemy again counts for nothing.
//   * The chain survives a ball dash in the middle of it, and ends on landing or
//     after `homingChainWindow` seconds without a hit.
//
// TARGETING (the "sensor"): a CONE around the crosshair line. A target counts if it is
// within the cone's radius at its distance (`homingAimRadius` for a close enemy, widening
// to `homingAimRadiusFar` at `homingRange`), in front of Shadow, and with a clear line
// of sight. Press F3 in play mode for the debug overlay; [ ] near radius, ; ' far radius,
// - = range, all live.
// ============================================================================
public enum HomingVerdict { Chosen, Valid, OutsideAimZone, BehindShadow, Blocked, SameTargetLockout }

// One line of the sensor's debug report (drawn by ShadowHud).
public struct HomingDebugEntry
{
    public Vector3 point;       // the target's centre
    public float aimDistance;   // metres from the crosshair line
    public float allowedRadius; // how far from the line this enemy was ALLOWED to be (cone radius at its distance)
    public float distance;      // metres from Shadow
    public HomingVerdict verdict;
}

public partial class ShadowMotor
{
    [Header("Homing Sensor (what can be locked onto)")]
    public float homingRange = 25f;              // max distance from Shadow to a target
    public float homingAimRadius = 2f;           // metres off the crosshair line that still counts for a CLOSE enemy (about one character length)
    public float homingAimRadiusFar = 5f;        // ...and for an enemy at max range. The zone is a cone: it widens from near to this.
    public float homingSameTargetLockout = 1.2f; // can't re-lock the enemy you just hit for this long (covers the whole bounce arc)
    public bool homingDebug;                     // evaluate the sensor even on the ground + record the report (F3 in play mode)

    [Header("Homing Attack (flight)")]
    public float homingSpeed = 48f;
    public float homingMaxTime = 0.6f;           // gives up if it hasn't connected by then
    public float homingHitDistance = 1.1f;       // counts as a hit this close to the target's surface
    public float homingDamage = 25f;
    public float homingBounceSpeed = 11f;        // upward speed after a hit: "gains a little height"
    public float homingBounceBack = 5f;          // push AWAY from the enemy (m/s), along the attack direction. Always the same, so the bounce is predictable
    public float homingResolveTime = 0.3f;       // seconds after a hit during which the player only gets a fraction of air control...
    [Range(0f, 1f)]
    public float homingResolveControl = 0.2f;    // ...this fraction (0 = locked on the bounce, 1 = full control)

    [Header("Ball Dash (no target): a MOVEMENT tech, no damage")]
    public float airDashDistance = 14f;          // reach in metres (was 9)
    public float airDashSpeed = 70f;             // 14 m takes about 0.2 s
    public float airDashExitSpeed = 14f;         // speed carried out of the dash
    public float airDashRise = 1.4f;             // metres of height gained DURING the dash (rises as it travels)
    public float airDashExitLift = 7f;           // upward speed when the dash ends: about +0.7 m more. Together ~ a double jump (2.2 m)
    public float airPressMinTime = 0.12f;        // seconds after leaving the ground before a second press counts
    public float dashMinGroundDistance = 1.2f;   // an untargeted dash needs this much height above the floor

    [Header("Stamina (= the boost gauge)")]
    public float airDashStaminaCost = 50f;       // 50 of 100 = two dashes from a full gauge
    public float homingStaminaCost = 0f;         // 0 = homing is free (so you can't get stuck at 0 stamina)
    public float homingChainWindow = 3f;         // the chain resets if this long passes without a homing hit
    // Fraction of the full gauge regained by the 1st, 2nd, 3rd... homing hit in a chain.
    // Past the last entry, the last value keeps being used. Edit freely in the Inspector.
    public float[] homingRegainByChain = { 0f, 0.08f, 0.2f, 0.4f, 0.667f };

    // ---- Read by the HUD / camera ----
    public bool IsBallDashing => Mode == ShadowMoveMode.BallDash;
    public bool HasHomingTarget => homingCollider != null;
    public bool HomingTargetUsable => homingCollider != null && Mode == ShadowMoveMode.Normal
                                      && IsAirborne && boost >= homingStaminaCost;
    public Vector3 HomingTargetPoint => homingCollider != null ? homingCollider.bounds.center : Vector3.zero;
    public readonly List<HomingDebugEntry> HomingDebug = new List<HomingDebugEntry>(32);

    public float Stamina => boost;          // stamina IS the boost gauge
    public float StaminaMax => boostMax;
    // Radius of the aim cone at a given distance from Shadow (near value up close, far value at max range).
    public float HomingAimRadiusAt(float dist)
        => Mathf.Lerp(homingAimRadius, Mathf.Max(homingAimRadius, homingAimRadiusFar), Mathf.Clamp01(dist / homingRange));
    public float AirDashStaminaCost => airDashStaminaCost;
    public int HomingChain => homingChain;
    public float LastStaminaGain { get; private set; }
    public float LastStaminaGainTime { get; private set; } = -10f;

    // ---- Private state ----
    int homingChain;
    float homingResolveUntil;                  // Time.time until which air control is reduced after a homing hit
    float lastHomingHitTime = -10f;
    IDamageable lastHomingTarget;
    float lastHomingTargetTime = -10f;

    Collider homingCollider;                   // current lock-on candidate (updated every frame)
    IDamageable homingDamageable;

    Collider ballTargetCollider;               // the target of the dash in progress
    IDamageable ballTargetDamageable;
    bool ballHoming;
    Vector3 ballDir = Vector3.forward;
    float ballRemaining, ballElapsed;
    readonly HashSet<IDamageable> seenTargets = new HashSet<IDamageable>();

    // ---------------------------------------------------------------------
    // STAMINA
    // ---------------------------------------------------------------------
    // A homing chain that has gone cold is over. (Stamina itself is the boost gauge,
    // which ShadowMotor.UpdateBoost refills.)
    void UpdateHomingChain()
    {
        if (homingChain > 0 && Time.time - lastHomingHitTime > homingChainWindow) homingChain = 0;
    }

    // Adds to the boost gauge (a big enough gain also ends a boost lockout: UpdateBoost checks that).
    void GainStamina(float amount)
    {
        if (amount <= 0f) return;
        float before = boost;
        boost = Mathf.Min(boostMax, boost + amount);
        LastStaminaGain = boost - before;
        LastStaminaGainTime = Time.time;
    }

    // ---------------------------------------------------------------------
    // THE SENSOR
    // ---------------------------------------------------------------------
    // Picks the target closest to the crosshair LINE that passes every check.
    // Runs while a homing attack would be possible, or always when homingDebug is on.
    void UpdateHomingTarget()
    {
        homingCollider = null;
        homingDamageable = null;
        if (homingDebug) HomingDebug.Clear();

        bool canHome = Mode == ShadowMoveMode.Normal && IsAirborne && boost >= homingStaminaCost;
        if (!canHome && !homingDebug) return;

        Vector3 chest = transform.position + Vector3.up;
        Transform camT = rig.cam.transform;
        Vector3 camPos = camT.position;
        Vector3 camFwd = camT.forward;

        int n = Physics.OverlapSphereNonAlloc(chest, homingRange, overlapBuf, aimMask,
                                              QueryTriggerInteraction.Ignore);
        seenTargets.Clear();
        float bestScore = float.MaxValue;
        int chosenIndex = -1;

        for (int i = 0; i < n; i++)
        {
            Collider col = overlapBuf[i];
            if (col.transform.IsChildOf(transform)) continue;

            IDamageable d = col.GetComponentInParent<IDamageable>();
            if (d == null || !seenTargets.Add(d)) continue; // one verdict per enemy, not per collider

            Vector3 center = col.bounds.center;
            Vector3 toCenter = center - camPos;
            float along = Vector3.Dot(toCenter, camFwd);                       // how far in front of the camera
            float aimDist = (toCenter - camFwd * along).magnitude;             // metres off the crosshair line
            float dist = (center - chest).magnitude;
            float allowed = HomingAimRadiusAt(dist);                           // the cone is narrow up close, wider far away

            HomingVerdict v = HomingVerdict.Valid;
            if (along <= 0f || Vector3.Dot(camFwd, center - chest) <= 0f)
                v = HomingVerdict.BehindShadow;
            else if (aimDist > allowed)
                v = HomingVerdict.OutsideAimZone;
            else if (d == lastHomingTarget && Time.time - lastHomingTargetTime < homingSameTargetLockout)
                v = HomingVerdict.SameTargetLockout;
            else if (dist > 0.01f
                     && RaycastIgnoringSelf(chest, (center - chest) / dist, dist, out RaycastHit hit)
                     && hit.collider.GetComponentInParent<IDamageable>() != d)
                v = HomingVerdict.Blocked;                                     // something solid is in the way

            float score = aimDist / allowed; // 0 = dead centre of the crosshair line, 1 = on the edge of the cone
            if (v == HomingVerdict.Valid && score < bestScore)
            {
                bestScore = score;
                homingCollider = col;
                homingDamageable = d;
                chosenIndex = homingDebug ? HomingDebug.Count : -1;
            }

            if (homingDebug)
                HomingDebug.Add(new HomingDebugEntry { point = center, aimDistance = aimDist, allowedRadius = allowed, distance = dist, verdict = v });
        }

        // Mark the winner in the report.
        if (homingDebug && chosenIndex >= 0)
        {
            HomingDebugEntry e = HomingDebug[chosenIndex];
            e.verdict = HomingVerdict.Chosen;
            HomingDebug[chosenIndex] = e;
        }
    }

    // ---------------------------------------------------------------------
    // THE JUMP PRESS IN THE AIR
    // ---------------------------------------------------------------------
    // Priority: wall jump (mid wall run) > homing > wall kick > ball dash.
    void TryAirPressAbility()
    {
        if (Mode == ShadowMoveMode.WallRun)
        {
            DoWallJump(wallNormal, wallCollider);
            return;
        }
        if (Mode != ShadowMoveMode.Normal || !IsAirborne) return;
        if (Time.time - lastTouchGroundTime < airPressMinTime) return;

        if (HasHomingTarget && boost >= homingStaminaCost) { StartHoming(); return; }

        if (wallJumpsLeft > 0 && TryFindWallForKick(out RaycastHit kick))
        {
            DoWallJump(kick.normal, kick.collider);
            return;
        }

        if (boost >= airDashStaminaCost && HighEnoughForDash()) StartAirDash();
    }

    bool HighEnoughForDash()
        => !Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, dashMinGroundDistance,
                            aimMask, QueryTriggerInteraction.Ignore);

    void StartHoming()
    {
        if (homingStaminaCost > 0f) { boost = Mathf.Max(0f, boost - homingStaminaCost); lastBoostTime = Time.time; }
        ballHoming = true;
        ballTargetCollider = homingCollider;
        ballTargetDamageable = homingDamageable;
        ballElapsed = 0f;
        Vector3 toTarget = homingCollider.bounds.center - (transform.position + Vector3.up);
        ballDir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward;
        HomingStartedThisFrame = true;
        BeginBallDash();
    }

    // Shoots along the crosshair direction, but with limited up/down so it stays a dash, not a launch.
    void StartAirDash()
    {
        boost -= airDashStaminaCost;       // spends the boost gauge...
        lastBoostTime = Time.time;         // ...and pauses its refill, like boosting does
        Vector3 f = rig.cam.transform.forward;
        f.y = Mathf.Clamp(f.y, -0.6f, 0.1f); // aiming down dives; the upward part comes from airDashRise, not the aim
        ballDir = f.normalized;
        ballHoming = false;
        ballTargetCollider = null;
        ballTargetDamageable = null;
        ballRemaining = airDashDistance;
        BeginBallDash();
    }

    void BeginBallDash()
    {
        Mode = ShadowMoveMode.BallDash;
        BallDashStartedThisFrame = true;
        lastJumpPressTime = -10f;          // the press is used up: don't also buffer a jump

        ShadowAfterimage.Spawn(visual, chaosColor, ghostLife);
        lastGhostPos = transform.position;
    }

    // ---------------------------------------------------------------------
    // THE DASH ITSELF
    // ---------------------------------------------------------------------
    // Runs every frame while Mode is BallDash. Sets horizontalVel AND verticalVel (no gravity).
    void BallDashStep()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 chest = transform.position + Vector3.up;
        Vector3 vel;

        if (ballHoming)
        {
            // Target destroyed mid-flight: just end the dash.
            if (ballTargetCollider == null) { EndBallDash(); return; }

            // Close enough to the target's surface = a hit.
            Vector3 closest = ballTargetCollider.ClosestPoint(chest);
            if ((closest - chest).magnitude <= homingHitDistance) { HomingHit(closest); return; }

            // Re-aim every frame: this is what makes it HOME rather than fly straight.
            ballDir = (ballTargetCollider.bounds.center - chest).normalized;
            vel = ballDir * homingSpeed;

            ballElapsed += dt;
            if (ballElapsed > homingMaxTime) { EndBallDash(); return; }
        }
        else
        {
            // Distance-based like the dodge: always covers exactly airDashDistance.
            float step = Mathf.Min(airDashSpeed * dt, ballRemaining);
            vel = ballDir * (step / dt);
            ballRemaining -= step;
            // Height boost: rises by airDashRise over the time the dash takes (distance / speed).
            vel.y += airDashRise / Mathf.Max(0.01f, airDashDistance / airDashSpeed);
        }

        horizontalVel = new Vector3(vel.x, 0f, vel.z);
        verticalVel = vel.y;
    }

    // After the move: leave the trail, and end an untargeted dash when it has finished or hit something solid.
    void BallDashPostMove(CollisionFlags flags)
    {
        DropTrailAfterimage();
        bool blocked = (flags & (CollisionFlags.Sides | CollisionFlags.Above)) != 0;
        if (!ballHoming && (ballRemaining <= 0.001f || blocked)) EndBallDash();
    }

    // HOMING HIT: damage, bounce UP so Shadow gains a little height, and (if it's a NEW enemy)
    // extend the chain and regain stamina by the chain's table entry.
    void HomingHit(Vector3 point)
    {
        if (ballTargetDamageable != null) ballTargetDamageable.TakeDamage(homingDamage, point, DamageKind.Homing);
        HomingHitThisFrame = true;

        // Chain: only a different enemy than the last one counts, so you can't farm stamina on one dummy.
        if (Time.time - lastHomingHitTime > homingChainWindow) homingChain = 0;
        bool newTarget = ballTargetDamageable != lastHomingTarget;
        if (newTarget)
        {
            homingChain++;
            if (homingRegainByChain != null && homingRegainByChain.Length > 0)
            {
                int index = Mathf.Min(homingChain - 1, homingRegainByChain.Length - 1);
                GainStamina(boostMax * homingRegainByChain[index]);
            }
        }
        lastHomingHitTime = Time.time;
        lastHomingTarget = ballTargetDamageable;
        lastHomingTargetTime = Time.time;

        // PREDICTABLE RESOLUTION: every hit bounces UP by homingBounceSpeed and AWAY from the enemy by
        // homingBounceBack, along the line of the attack. For a nearly vertical attack there is no
        // meaningful "away", so he backs off the way the camera faces. For a short time afterwards the
        // player only has a fraction of air control (homingResolveControl), so it plays out the same way
        // every time and he can't drift back over the same enemy.
        Vector3 away = new Vector3(-ballDir.x, 0f, -ballDir.z);
        if (away.sqrMagnitude < 0.05f)
            away = -(Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward);
        horizontalVel = away.normalized * homingBounceBack;
        verticalVel = homingBounceSpeed;
        homingResolveUntil = Time.time + homingResolveTime;

        Mode = ShadowMoveMode.Normal;
        carryMomentum = false;
    }

    // The dash ended without a hit (finished, timed out, blocked, or target vanished).
    void EndBallDash()
    {
        Vector3 flat = new Vector3(ballDir.x, 0f, ballDir.z);
        horizontalVel = flat.sqrMagnitude > 0.0001f ? flat.normalized * airDashExitSpeed : Vector3.zero;
        // An untargeted dash ends with an upward kick (the double-jump feel). A homing dash that fizzled doesn't.
        verticalVel = ballHoming ? 0f : airDashExitLift;
        Mode = ShadowMoveMode.Normal;
        carryMomentum = true;
    }
}