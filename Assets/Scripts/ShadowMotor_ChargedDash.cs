using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// ShadowMotor_ChargedDash.cs                                         (v0.1)
// PART OF: ShadowMotor (a partial class). Do NOT attach this file to anything.
//
// CHARGED SPIN DASH
//   START    Stand (nearly) still on the ground and hold Ctrl + Shift for a moment.
//   CHARGE   Shadow curls into a spinning ball and revs. The stamina bar turns gold and fills.
//            Hold W/A/S/D to point the launch that way, or let go of the keys to launch where
//            the camera looks. Taking a hit does NOT interrupt the rev.
//   LAUNCH   Let go of SHIFT. (Let go of Ctrl instead to abort. Jump or Chaos warp also bail out.)
//            Releasing early still launches, but weaker. Below chargeMinFraction it just aborts.
//   BURST    A fast decaying burst, slightly drifty to steer, that damages everything it touches.
//            The stamina gauge is the FUEL: a full gauge gives the longest burst.
//   CRASH    Hitting a wall or a tough enemy that survives the hit stops the dash dead.
//   END      Burst spent -> rolls out into a normal slide. Ctrl released -> ends immediately.
// ============================================================================
public partial class ShadowMotor
{
    [Header("Charged Spin Dash: the rev (stand still, hold Ctrl + Shift)")]
    public float chargeStartMaxSpeed = 2.5f;   // must be moving slower than this to begin: stopping IS the price
    public float chargeCommitDelay = 0.1f;     // hold both keys this long before the rev starts (no accidental revs)
    public float chargeTime = 1f;              // seconds from 0 to a full charge. The gauge is topped up in sync.
    [Range(0f, 1f)]
    public float chargeMinFraction = 0.25f;    // release Shift before this much charge and it just aborts
    public float chargeAimDegPerSec = 360f;    // how fast the ball turns to face the launch direction while revving
    public float chargeRelockTime = 0.6f;      // after a dash ends, wait this long before the next rev can begin

    [Header("Charged Spin Dash: the launch")]
    public float chargedPeakSpeed = 42f;       // launch speed at FULL charge (spinDashSpeed at the minimum charge, scaled between)
    public float chargedBurstMaxTime = 1.5f;   // burst length from a FULL gauge (shorter gauge = shorter burst)
    // Speed over the burst: X = 0 (launch) to 1 (burst over), Y = 1 (peak speed) to 0 (spinDashSpeed).
    public AnimationCurve chargedSpeedCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    public float chargedTurnDegPerSec = 120f;  // how fast the ball's HEADING turns toward your input
    public float chargedGripDegPerSec = 50f;   // how fast the actual TRAVEL direction follows the heading. LOWER = more drift
    public float chargedDashDamage = 45f;      // damage at full charge (spinDashDamage at the minimum), once per enemy per launch
    public float chargedHitRadius = 1.4f;
    public int chargedDashHealthDrops = 2;     // most health pickups ONE launch can produce, however many it kills
    public float chargedCrashGrace = 0.15f;    // crash checks start this long after launch
    [Range(0f, 1f)]
    public float chargedCrashFraction = 0.4f;  // crash if real speed falls below this fraction of intended speed

    [Header("Charged Spin Dash: audio hooks (all optional)")]
    public AudioSource chargeAudio;            // give it a LOOPING rev clip; its pitch rises with the charge
    public AudioClip chargeReadyClip;          // one-shot when the charge hits full
    public AudioClip launchClip;               // one-shot on release
    public Vector2 chargePitchRange = new Vector2(0.8f, 2f);

    // ---- Read by other scripts ----
    public bool IsCharging => Mode == ShadowMoveMode.Charging;
    public bool IsChargedDashing => Mode == ShadowMoveMode.ChargedDash;
    public bool IsLowProfile => IsSliding || IsSpinDashing || IsCharging || IsChargedDashing; // camera: ball / slide framing
    public float ChargeProgress => chargeAmount;                 // 0..1, for HUD / camera / audio
    public float ChargeMinFraction => chargeMinFraction;
    public bool ChargedDashLaunchedThisFrame { get; private set; } // one-frame flag, read by the camera
    // 0..1 while a charged dash is running. FUTURE MELEE HOOK: a melee strike can read this to turn leftover speed into damage.
    public float ChargedDashSpeed01 => IsChargedDashing ? Mathf.InverseLerp(spinDashSpeed, chargedPeakSpeed, slideSpeed) : 0f;

    // ---- Private state ----
    float chargeAmount;          // 0..1
    float chargeStartBoost;      // gauge when the rev began (so the gauge can reach full exactly at full charge)
    float chargeArm;             // how long the start conditions have been held
    float chargeLockoutUntil;    // Time.time before which a new rev can't begin
    bool chargeReadyPlayed;
    Vector3 chargedHeading = Vector3.forward;   // the way the ball POINTS
    Vector3 chargedVelDir = Vector3.forward;    // the way the ball actually TRAVELS (lags the heading = drift)
    float chargedPeakNow, chargedDamageNow;     // this launch's peak speed and damage (from the charge level)
    float chargedBurstTotal, chargedBurstLeft, chargedDrainPerSec, chargedAge;
    readonly HashSet<IDamageable> chargedHit = new HashSet<IDamageable>();

    // Called every frame from Update. Clears the one-frame flag and decides whether a rev begins.
    void ChargeDashUpdate()
    {
        ChargedDashLaunchedThisFrame = false;

        bool wants = Mode == ShadowMoveMode.Normal
                  && input.SlamHeld && input.BoostHeld          // Ctrl + Shift
                  && GroundedForSlide() && verticalVel < 0.5f   // on the floor, not mid-jump
                  && horizontalVel.magnitude <= chargeStartMaxSpeed // nearly stopped
                  && Time.time >= chargeLockoutUntil;

        chargeArm = wants ? chargeArm + Time.deltaTime : 0f;
        if (!wants || chargeArm < chargeCommitDelay) return;

        BeginCharge();
    }

    void BeginCharge()
    {
        Mode = ShadowMoveMode.Charging;
        chargeAmount = 0f;
        chargeStartBoost = boost;
        chargeArm = 0f;
        chargeReadyPlayed = false;
        carryMomentum = false;
        chargedHeading = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
        SetControllerHeight(slideHeight);   // a ball is small: fits under low gaps

        if (chargeAudio != null)
        {
            chargeAudio.loop = true;
            chargeAudio.pitch = chargePitchRange.x;
            chargeAudio.Play();
        }
    }

    // Runs every frame while Mode is Charging. Sets horizontalVel.
    void ChargeStep(Vector3 wishDir, bool hasInput)
    {
        float dt = Time.deltaTime;

        // Lost the floor (knocked or walked off a ledge): the rev is lost.
        if (IsAirborne)
        {
            StopChargeAudio();
            chargeAmount = 0f;
            ExitSlide(true);
            NormalHorizontal(wishDir, hasInput);
            return;
        }

        // Plant: brake to a standstill (nearly stopped on entry, so this is a short skid).
        horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, groundDecel * 2f * dt);

        // Ctrl released (alone, or together with Shift): abort.
        if (!input.SlamHeld) { CancelCharge(); return; }

        // Shift released: LAUNCH if there is enough charge, else the tap was just an abort.
        if (!input.BoostHeld)
        {
            if (chargeAmount >= chargeMinFraction) LaunchChargedDash(); else CancelCharge();
            return;
        }

        // ---- The rev ----
        chargeAmount = Mathf.Min(1f, chargeAmount + dt / chargeTime);

        // The stamina gauge fills IN SYNC, reaching full exactly when the charge does. One bar to read.
        boost = Mathf.Min(boostMax, boost + (boostMax - chargeStartBoost) / chargeTime * dt);
        lastBoostTime = Time.time; // also keeps normal regen from stacking on top

        // Point the ball: input direction if keys are held, otherwise where the camera looks.
        Vector3 camFwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
        chargedHeading = SteerDir(chargedHeading, hasInput ? wishDir : camFwd, chargeAimDegPerSec);

        if (chargeAudio != null)
            chargeAudio.pitch = Mathf.Lerp(chargePitchRange.x, chargePitchRange.y, chargeAmount);
        if (chargeAmount >= 1f && !chargeReadyPlayed)
        {
            chargeReadyPlayed = true;
            if (chargeAudio != null && chargeReadyClip != null) chargeAudio.PlayOneShot(chargeReadyClip);
        }
    }

    void LaunchChargedDash()
    {
        float t = chargeAmount;
        chargedPeakNow = Mathf.Lerp(spinDashSpeed, chargedPeakSpeed, t);   // never slower than a normal spin dash
        chargedDamageNow = Mathf.Lerp(spinDashDamage, chargedDashDamage, t);

        // The gauge is the FUEL. Burst length scales with how much is in it, and it drains to empty over the burst.
        chargedBurstTotal = Mathf.Max(0.3f, chargedBurstMaxTime * boost / boostMax);
        chargedBurstLeft = chargedBurstTotal;
        chargedDrainPerSec = boost / chargedBurstTotal;

        chargedVelDir = chargedHeading;
        chargedAge = 0f;
        slideAirTime = 0f;
        slideSpeed = chargedPeakNow;
        chargedHit.Clear();
        EnemyDrops.ChargedHealthBudget = chargedDashHealthDrops;
        chargeLockoutUntil = Time.time + chargedBurstTotal + chargeRelockTime;
        lastGhostPos = transform.position;   // the afterimage trail starts here

        horizontalVel = chargedVelDir * slideSpeed;
        Mode = ShadowMoveMode.ChargedDash;
        ChargedDashLaunchedThisFrame = true;

        StopChargeAudio();
        if (chargeAudio != null && launchClip != null) chargeAudio.PlayOneShot(launchClip);
    }

    // Stand back up (if there is headroom) and forget the rev.
    void CancelCharge()
    {
        StopChargeAudio();
        chargeAmount = 0f;
        horizontalVel = Vector3.zero;
        if (HasHeadroom()) ExitSlide(false);
        else { Mode = ShadowMoveMode.Slide; slideDir = chargedHeading; slideSpeed = 0f; } // stay low until there is room
    }

    // Runs every frame while Mode is ChargedDash. Sets horizontalVel.
    void ChargedDashStep(Vector3 wishDir, bool hasInput)
    {
        float dt = Time.deltaTime;
        chargedAge += dt;

        // Off a ledge for real (not a seam hop): hand over to air movement, keeping the speed.
        slideAirTime = cc.isGrounded ? 0f : slideAirTime + dt;
        if (slideAirTime > slideAirGrace)
        {
            horizontalVel = chargedVelDir * slideSpeed;
            ExitSlide(true);
            NormalHorizontal(wishDir, hasInput);
            return;
        }

        // CRASH: the CharacterController reports the speed it ACTUALLY moved at last frame. If a wall (or an
        // enemy that survived the hit) is eating most of it, the dash is over. Sliding ALONG a wall is fine.
        if (chargedAge > chargedCrashGrace)
        {
            Vector3 actual = cc.velocity; actual.y = 0f;
            if (actual.magnitude < slideSpeed * chargedCrashFraction)
            {
                horizontalVel = Vector3.zero;
                if (HasHeadroom()) ExitSlide(false);
                else { Mode = ShadowMoveMode.Slide; slideDir = chargedVelDir; slideSpeed = 0f; }
                return;
            }
        }

        // Steering with DRIFT: the heading follows your input quickly, but the travel direction follows the
        // heading slowly, so the ball slides wide through turns.
        if (hasInput) chargedHeading = SteerDir(chargedHeading, wishDir, chargedTurnDegPerSec);
        chargedVelDir = SteerDir(chargedVelDir, chargedHeading, chargedGripDegPerSec);

        // Speed: a fast launch that eases down toward spin-dash speed. The gauge drains to empty over the burst.
        chargedBurstLeft -= dt;
        float t = 1f - Mathf.Clamp01(chargedBurstLeft / chargedBurstTotal);
        slideSpeed = Mathf.Lerp(spinDashSpeed, chargedPeakNow, chargedSpeedCurve.Evaluate(t));
        boost = Mathf.Max(0f, boost - chargedDrainPerSec * dt);
        lastBoostTime = Time.time;

        DamageAlongChargedDash();
        DropTrailAfterimage();

        // Follow the floor (same technique as the slide): hug slopes, stay glued to seams.
        bool onGround = RaycastIgnoringSelf(transform.position + Vector3.up * 0.5f, Vector3.down, 1.4f, out RaycastHit ground)
                        && ground.normal.y > 0.5f;
        Vector3 v = chargedVelDir * slideSpeed;
        if (onGround) v = Vector3.ProjectOnPlane(v, ground.normal);
        horizontalVel = new Vector3(v.x, 0f, v.z);
        if (onGround)
        {
            float push = slideGroundPush;
            if (!cc.isGrounded && ground.distance - 0.5f <= slideSnapDistance) push = slideSnapSpeed;
            verticalVel = v.y - push;
        }

        // Burst spent: the gauge is empty (no lockout: it ended exactly as planned). Roll out as a plain slide.
        if (chargedBurstLeft <= 0f)
        {
            boost = 0f;
            chargeAmount = 0f;
            slideDir = chargedVelDir;
            slideSpeed = spinDashSpeed;
            Mode = ShadowMoveMode.Slide;
            return;
        }

        // Ctrl released: end it, like a normal spin dash (only if there is room to stand).
        if (!input.SlamHeld && HasHeadroom()) ExitSlide(false);
    }

    // Contact damage. Each enemy is hit ONCE per launch (no tick damage), for the damage this charge level earned.
    void DamageAlongChargedDash()
    {
        Vector3 center = transform.position + Vector3.up * 0.5f;
        int n = Physics.OverlapSphereNonAlloc(center, chargedHitRadius, overlapBuf, aimMask,
                                              QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = overlapBuf[i];
            if (col.transform.IsChildOf(transform)) continue;

            IDamageable d = col.GetComponentInParent<IDamageable>();
            if (d == null || !chargedHit.Add(d)) continue;   // Add returns false if this enemy was already hit

            d.TakeDamage(chargedDamageNow, col.ClosestPoint(center), DamageKind.ChargedDash);
        }
    }

    void StopChargeAudio()
    {
        if (chargeAudio == null) return;
        if (chargeAudio.isPlaying && chargeAudio.loop) chargeAudio.Stop();
        chargeAudio.pitch = 1f;
    }

    // Called by ResetMotion (respawn).
    void ResetChargeDash()
    {
        chargeAmount = 0f;
        chargeArm = 0f;
        chargeLockoutUntil = 0f;
        chargedHit.Clear();
        StopChargeAudio();
    }
}