using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// ShadowMotor_Slide.cs                                               (v0.5)
// PART OF: ShadowMotor (a partial class). Do NOT attach this file to anything.
//
// SLIDE      Ctrl on the ground while moving. A low, long glide. Keeps momentum,
//            slides under low gaps, can steer a little, CAN shoot.
// SPIN DASH  Ctrl while holding boost (Shift). Ball form: faster, steers harder,
//            DAMAGES what it touches, CANNOT shoot, and the boost gauge drains
//            at half speed so boost lasts longer.
//
// Both end when Ctrl is released (or, for a slide, when it runs out of speed).
// Jumping out of either keeps your speed (slide-jump, the Titanfall trick).
// You can also press boost mid-slide to upgrade it into a spin dash.
// ============================================================================
public partial class ShadowMotor
{
    [Header("Slide (Ctrl on the ground)")]
    public float slideHeight = 1f;           // CharacterController height while sliding (fits under low gaps)
    public float slideStartSpeed = 12f;      // minimum speed the slide starts at
    public float slideDecel = 7f;            // how fast a slide loses speed
    public float slideMinSpeed = 3.5f;       // below this the slide ends
    public float slideSteerDegPerSec = 60f;  // gentle steering while sliding
    public float slideSlopeAccel = 40f;      // slope bonus: m/s per second on a 90-degree slope. A 20-degree hill gives about 14
    public float slideMaxSpeed = 30f;        // top speed a slide can reach downhill
    public float slideGroundGrace = 0.15f;   // a slide may START this long after the ground was last touched (forgives ground-contact flicker)
    public float slideBufferTime = 0.15f;    // Ctrl pressed up to this long BEFORE a slide is possible still starts one
    public float slideAirGrace = 0.35f;      // a slide is only cancelled after being airborne this long (mini hops over seams don't count)
    public float slideGroundPush = 6f;       // downward push while sliding that keeps the collider glued to the floor
    public float slideSnapDistance = 0.4f;   // after a hop, if the floor is this close beneath, snap back down onto it
    public float slideSnapSpeed = 12f;       // ...with this downward speed

    [Header("Spin Dash (Ctrl + boost)")]
    public float spinDashStartSpeed = 18f;
    public float spinDashSpeed = 24f;        // top speed (above boostSpeed)
    public float spinDashAccel = 40f;
    public float spinDashSteerDegPerSec = 110f;
    public float spinDashDrainMultiplier = 0.5f; // gauge drains at this fraction of normal boost: lasts longer
    public float spinDashMinGauge = 10f;     // gauge needed to start or upgrade into a spin dash
    public float spinDashDamage = 15f;
    public float spinDashHitInterval = 0.35f;// seconds before the same enemy can be hit again
    public float spinDashHitRadius = 1.0f;

    public bool IsSliding => Mode == ShadowMoveMode.Slide;
    public bool IsSpinDashing => Mode == ShadowMoveMode.SpinDash;

    float standingHeight;                    // remembered from the CharacterController at startup
    Vector3 slideDir;                        // current slide / spin direction (flat, unit length)
    float slideSpeed;
    float slideAirTime;                      // how long the current slide has been off the ground
    float slideBufferedUntil;                // Time.time until which a pressed-but-not-started slide keeps retrying
    readonly Dictionary<IDamageable, float> spinHitTimes = new Dictionary<IDamageable, float>();

    // Starts a slide, or a spin dash if boost is held and there is gauge.
    void TryStartSlide(Vector3 wishDir, bool hasInput)
    {
        if (Mode != ShadowMoveMode.Normal || !GroundedForSlide()) return;

        float speed = horizontalVel.magnitude;
        if (speed < 2f && !hasInput) return; // standing still: nothing to slide

        slideDir = speed > 1f ? horizontalVel / speed : wishDir.normalized;
        slideDir.y = 0f;

        bool spin = input.BoostHeld && !boostLocked && boost >= spinDashMinGauge;
        if (spin) BeginSpinDash(); else Mode = ShadowMoveMode.Slide;

        slideSpeed = Mathf.Max(speed, spin ? spinDashStartSpeed : slideStartSpeed);
        carryMomentum = false;
        SetControllerHeight(slideHeight);
        slideAirTime = 0f;
    }

    // "Standing on the ground" for the purpose of starting a slide. CharacterController.isGrounded can
    // flicker false for a frame (seams between floor pieces, tiny bumps), so we also accept ground that
    // was touched a moment ago, as long as we are not in the middle of a jump.
    bool GroundedForSlide()
        => cc.isGrounded || (Time.time - lastTouchGroundTime <= slideGroundGrace && verticalVel < 3f);

    void BeginSpinDash()
    {
        Mode = ShadowMoveMode.SpinDash;
        spinHitTimes.Clear();
        slideSpeed = Mathf.Max(slideSpeed, spinDashStartSpeed);
    }

    // Runs every frame while Mode is Slide or SpinDash. Sets horizontalVel.
    void SlideStep(Vector3 wishDir, bool hasInput)
    {
        float dt = Time.deltaTime;

        // Tiny hops over seams and bumps are normal and must NOT cancel the slide. Only being off the
        // ground for slideAirGrace seconds (a real drop, like a ledge) hands over to air movement.
        slideAirTime = cc.isGrounded ? 0f : slideAirTime + dt;
        if (slideAirTime > slideAirGrace)
        {
            horizontalVel = slideDir * slideSpeed;
            ExitSlide(true);
            NormalHorizontal(wishDir, hasInput);
            return;
        }

        // Pressing boost mid-slide upgrades it into a spin dash.
        if (Mode == ShadowMoveMode.Slide && input.BoostHeld && !boostLocked && boost >= spinDashMinGauge)
            BeginSpinDash();

        // Steering (by yaw angle, so a full reversal can't flip into the vertical plane).
        float steer = Mode == ShadowMoveMode.SpinDash ? spinDashSteerDegPerSec : slideSteerDegPerSec;
        if (hasInput) slideDir = SteerDir(slideDir, wishDir, steer);

        // What is under us? Gives the slope for the speed bonus, and lets us follow the surface.
        bool onGround = RaycastIgnoringSelf(transform.position + Vector3.up * 0.5f, Vector3.down, 1.4f, out RaycastHit ground)
                        && ground.normal.y > 0.5f;
        Vector3 groundN = onGround ? ground.normal : Vector3.up;

        if (Mode == ShadowMoveMode.SpinDash)
        {
            // Spin dash: speeds up, drains the gauge slowly, damages what it touches.
            boost -= boostDrainPerSec * spinDashDrainMultiplier * dt;
            lastBoostTime = Time.time; // also delays gauge regen
            slideSpeed = Mathf.MoveTowards(slideSpeed, spinDashSpeed, spinDashAccel * dt);
            DamageAlongSpin();

            if (boost <= 0f)
            {
                boost = 0f;
                boostLocked = true;
                Mode = ShadowMoveMode.Slide; // out of gauge: drop back to a plain slide
            }
        }
        else
        {
            slideSpeed = Mathf.Max(0f, slideSpeed - slideDecel * dt);

            // SLOPE BONUS: downhill speeds a slide up, uphill drains it faster.
            if (onGround)
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundN); // points down the slope
                float slopeSin = downhill.magnitude;                              // 0 flat .. 1 vertical
                if (slopeSin > 0.001f)
                {
                    Vector3 downhillFlat = new Vector3(downhill.x, 0f, downhill.z).normalized;
                    float along = Vector3.Dot(slideDir, downhillFlat);            // +1 straight downhill, -1 uphill
                    slideSpeed = Mathf.Clamp(slideSpeed + slideSlopeAccel * slopeSin * along * dt, 0f, slideMaxSpeed);
                }
            }
        }

        // Move ALONG the surface, so a fast slide hugs a slope instead of launching off it.
        Vector3 v = slideDir * slideSpeed;
        if (onGround) v = Vector3.ProjectOnPlane(v, groundN);
        horizontalVel = new Vector3(v.x, 0f, v.z);
        if (onGround)
        {
            // Keep the collider glued to the floor. A firm push (stronger than the walking -2) stops it
            // skipping over seams. If a hop already happened but the floor is right below, snap down harder.
            float push = slideGroundPush;
            if (!cc.isGrounded && ground.distance - 0.5f <= slideSnapDistance) push = slideSnapSpeed;
            verticalVel = v.y - push; // (gravity is added after this)
        }

        // Ending: Ctrl released, or a slide ran out of speed. Only stand up if there is headroom.
        bool tooSlow = Mode == ShadowMoveMode.Slide && slideSpeed < slideMinSpeed;
        if ((!input.SlamHeld || tooSlow) && HasHeadroom()) ExitSlide(false);
    }

    // Back to Normal and stand up. keepMomentum = true makes air steering keep your speed.
    void ExitSlide(bool keepMomentum)
    {
        Mode = ShadowMoveMode.Normal;
        SetControllerHeight(standingHeight);
        if (keepMomentum) carryMomentum = true;
    }

    // Resizes the CharacterController while keeping its feet on the ground.
    void SetControllerHeight(float h)
    {
        cc.height = h;
        cc.center = new Vector3(0f, h * 0.5f, 0f);
    }

    // Is there room for Shadow's full standing height where he is now?
    bool HasHeadroom() => CapsuleClearAt(transform.position);

    // Would a standing capsule with its feet at feetPos overlap any solid (other than Shadow)?
    bool CapsuleClearAt(Vector3 feetPos)
    {
        float r = cc.radius;
        Vector3 bottom = feetPos + Vector3.up * (r + 0.05f);
        Vector3 top = feetPos + Vector3.up * (standingHeight - r);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r * 0.9f, overlapBuf, aimMask,
                                               QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!overlapBuf[i].transform.IsChildOf(transform)) return false;
        return true;
    }

    // Spin dash contact damage. Each enemy can only be hit every spinDashHitInterval seconds.
    void DamageAlongSpin()
    {
        Vector3 center = transform.position + Vector3.up * 0.5f;
        int n = Physics.OverlapSphereNonAlloc(center, spinDashHitRadius, overlapBuf, aimMask,
                                              QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = overlapBuf[i];
            if (col.transform.IsChildOf(transform)) continue;

            IDamageable d = col.GetComponentInParent<IDamageable>();
            if (d == null) continue;
            if (spinHitTimes.TryGetValue(d, out float last) && Time.time - last < spinDashHitInterval) continue;

            spinHitTimes[d] = Time.time;
            d.TakeDamage(spinDashDamage, col.ClosestPoint(center), DamageKind.SpinDash);
        }
    }
}