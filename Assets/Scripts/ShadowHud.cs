using UnityEngine;

// ============================================================================
// ShadowHud.cs                                                       (v0.6)
// GOES ON: Player (root). It finds ShadowMotor, ShadowInput and ShadowTestWeapon on its own.
//
// JOB: Temporary HUD drawn with OnGUI so you need no Canvas yet:
//   * Crosshair at screen centre (this IS the aim point: the camera ray goes through it)
//   * Bottom left, top to bottom: MODE label, WARP charges, STAMINA bar (stamina IS the boost gauge)
//   * Ammo counters, bottom right
//   * Homing lock-on box (red = a homing attack would fire now)
//   * F3: HOMING SENSOR DEBUG OVERLAY (see DrawHomingDebug)
//        [ and ]  near aim radius    ; and '  far aim radius    - and =  range
// Replace with a proper UI later; nothing else depends on this script.
// ============================================================================
[RequireComponent(typeof(ShadowMotor))]
public class ShadowHud : MonoBehaviour
{
    [Header("Crosshair")]
    public Color crosshairColor = Color.white;
    public float armLength = 9f;   // pixels
    public float gap = 5f;         // empty space around the centre
    public float thickness = 2f;

    [Header("Bars")]
    public Color staminaColor = new Color(0.2f, 0.9f, 1f);       // cyan: enough for a ball dash
    public Color staminaLowColor = new Color(1f, 0.45f, 0.1f);   // orange: below the dash cost
    public Color warpColor = new Color(1f, 0.12f, 0.08f);        // Shadow red
    public Color warpRechargeColor = new Color(0.45f, 0.06f, 0.05f);

    [Header("Homing Sensor Debug")]
    public float debugRingDepth = 12f; // metres: depth the aim-zone ring is drawn at when nothing is locked

    ShadowMotor motor;
    ShadowInput input;
    ShadowTestWeapon weapon;   // optional
    GUIStyle ammoStyle, smallStyle;

    void Awake()
    {
        motor = GetComponent<ShadowMotor>();
        input = GetComponent<ShadowInput>();
        weapon = GetComponent<ShadowTestWeapon>();
    }

    // Live tuning of the homing sensor while the debug overlay is open.
    void Update()
    {
        if (input.DebugTogglePressed) motor.homingDebug = !motor.homingDebug;
        if (!motor.homingDebug) return;

        motor.homingAimRadius = Mathf.Max(0.25f, motor.homingAimRadius + 0.25f * input.DebugRadiusStep);
        motor.homingAimRadiusFar = Mathf.Max(motor.homingAimRadius, motor.homingAimRadiusFar + 0.5f * input.DebugFarRadiusStep);
        motor.homingRange = Mathf.Max(4f, motor.homingRange + 2f * input.DebugRangeStep);
    }

    void OnGUI()
    {
        EnsureStyles();
        DrawCrosshair();
        DrawResources();
        DrawAmmo();
        DrawLockOn();
        if (motor.homingDebug) DrawHomingDebug();
    }

    void EnsureStyles()
    {
        if (ammoStyle == null)
            ammoStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.LowerRight };
        if (smallStyle == null)
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
    }

    // ------------------------------------------------------------------
    void DrawCrosshair()
    {
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float o = 1f; // outline thickness so it reads on any background

        // Four arms: left, right, up, down. Black outline first, then the colour on top.
        Rect[] arms =
        {
            new Rect(cx - gap - armLength, cy - thickness * 0.5f, armLength, thickness),
            new Rect(cx + gap,             cy - thickness * 0.5f, armLength, thickness),
            new Rect(cx - thickness * 0.5f, cy - gap - armLength, thickness, armLength),
            new Rect(cx - thickness * 0.5f, cy + gap,             thickness, armLength),
        };
        foreach (Rect r in arms)
            Box(new Rect(r.x - o, r.y - o, r.width + o * 2f, r.height + o * 2f), Color.black);
        foreach (Rect r in arms)
            Box(r, crosshairColor);
    }

    // ------------------------------------------------------------------
    // Bottom-left stack. Everything is positioned from `stY` upward.
    void DrawResources()
    {
        const float x = 20f, w = 240f;
        Color bg = new Color(0f, 0f, 0f, 0.6f);
        float stY = Screen.height - 40f;

        // ---- STAMINA: the boost gauge. Boost, spin dash, slam and the ball dash all spend it. ----
        Rect st = new Rect(x, stY, w, 16f);
        Box(st, bg);
        bool canDash = motor.Stamina >= motor.AirDashStaminaCost;
        Color barColor = motor.IsBoostLocked ? Color.red : (canDash ? staminaColor : staminaLowColor);
        Box(new Rect(st.x + 2f, st.y + 2f, (st.width - 4f) * motor.BoostNormalized, st.height - 4f), barColor);
        float tickX = st.x + 2f + (st.width - 4f) * (motor.AirDashStaminaCost / motor.StaminaMax);
        Box(new Rect(tickX - 1f, st.y, 2f, st.height), Color.white); // white tick = cost of one ball dash
        Label(x + w + 8f, stY, motor.IsBoostLocked ? "STAMINA  LOCKED" : "STAMINA");

        // Homing chain counter and the last stamina gain, next to the label.
        if (motor.HomingChain > 0)
            Label(x + w + 110f, stY, "CHAIN x" + motor.HomingChain);
        if (Time.time - motor.LastStaminaGainTime < 1f && motor.LastStaminaGain > 0.5f)
            Label(x + w + 180f, stY, "+" + Mathf.RoundToInt(motor.LastStaminaGain));

        // ---- WARP CHARGES: one pip per charge; the next one fills as it recharges ----
        float warpY = stY - 20f;
        int max = motor.MaxDodgeCharges;
        const float pipGap = 4f;
        float pipW = (w - pipGap * (max - 1)) / max;
        for (int i = 0; i < max; i++)
        {
            Rect pip = new Rect(x + i * (pipW + pipGap), warpY, pipW, 14f);
            Box(pip, bg);
            float fill = i < motor.DodgeCharges ? 1f : (i == motor.DodgeCharges ? motor.DodgeRechargeProgress : 0f);
            Box(new Rect(pip.x + 2f, pip.y + 2f, (pip.width - 4f) * fill, pip.height - 4f),
                i < motor.DodgeCharges ? warpColor : warpRechargeColor);
        }
        Label(x + w + 8f, warpY, "WARP " + motor.DodgeCharges + "/" + max);

        // ---- MODE (debug) ----
        Label(x, warpY - 22f, "MODE  " + motor.Mode + (motor.DebugGrounded ? "" : "   (AIR)")); // AIR flickering during a slide = ground contact is hopping
    }

    void DrawAmmo()
    {
        if (weapon == null) return;

        GUI.color = Color.white;
        string text = weapon.infiniteAmmo
            ? "AMMO  INF"
            : $"AMMO  {weapon.PrimaryAmmo}  |  SHELLS  {weapon.AltAmmo}";
        GUI.Label(new Rect(Screen.width - 320f, Screen.height - 44f, 300f, 28f), text, ammoStyle);
    }

    // Square on the enemy Shadow's homing attack would lock onto.
    // Red = pressing Jump now would fire it. Grey = shown by the debug sensor but not usable right now.
    void DrawLockOn()
    {
        if (!motor.HasHomingTarget) return;

        Vector3 sp = motor.rig.cam.WorldToScreenPoint(motor.HomingTargetPoint);
        if (sp.z <= 0f) return; // behind the camera

        float x = sp.x;
        float y = Screen.height - sp.y; // GUI space has y pointing down
        float half = 26f, t = 3f;
        Color c = motor.HomingTargetUsable ? new Color(1f, 0.15f, 0.1f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.9f);
        Box(new Rect(x - half, y - half, half * 2f, t), c);             // top
        Box(new Rect(x - half, y + half - t, half * 2f, t), c);         // bottom
        Box(new Rect(x - half, y - half, t, half * 2f), c);             // left
        Box(new Rect(x + half - t, y - half, t, half * 2f), c);         // right
    }

    // ------------------------------------------------------------------
    // HOMING SENSOR DEBUG (F3)
    //   * The white RING around the crosshair is the aim CONE's size at the distance of the locked
    //     target (or debugRingDepth when nothing is locked). The cone is narrow for close enemies
    //     (homingAimRadius) and widens to homingAimRadiusFar at max range, so the ring grows as the
    //     enemy gets further from Shadow.
    //   * Every enemy in range gets a marker and a verdict:
    //       red CHOSEN       the one a homing attack would hit
    //       orange VALID     passes every check but another is closer to the crosshair
    //       grey OUTSIDE     too far from the crosshair line (the "edge of the screen" case)
    //       dark BEHIND      behind Shadow / the camera
    //       yellow BLOCKED   a wall is in the way
    //       purple LOCKOUT   the enemy you just hit
    void DrawHomingDebug()
    {
        Camera cam = motor.rig.cam;
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        // Aim-zone ring.
        float depth = debugRingDepth;
        if (motor.HasHomingTarget)
            depth = Mathf.Max(1f, Vector3.Dot(motor.HomingTargetPoint - cam.transform.position, cam.transform.forward));
        float pxPerMetre = Screen.height / (2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        // The cone's radius depends on the distance from SHADOW, so measure that for the ring.
        float zoneDist = motor.HasHomingTarget
            ? (motor.HomingTargetPoint - (motor.transform.position + Vector3.up)).magnitude
            : debugRingDepth;
        float ringRadius = motor.HomingAimRadiusAt(zoneDist);
        DrawRing(new Vector2(cx, cy), ringRadius * pxPerMetre, new Color(1f, 1f, 1f, 0.55f));
        Label(cx + 8f, cy - 28f - ringRadius * pxPerMetre,
              "aim cone at " + zoneDist.ToString("0") + "m: " + ringRadius.ToString("0.0") + "m radius");

        // One marker per evaluated enemy.
        int chosen = 0;
        foreach (HomingDebugEntry e in motor.HomingDebug)
        {
            if (e.verdict == HomingVerdict.Chosen) chosen++;
            Vector3 sp = cam.WorldToScreenPoint(e.point);
            if (sp.z <= 0f) continue;
            float x = sp.x, y = Screen.height - sp.y;
            Box(new Rect(x - 5f, y - 5f, 10f, 10f), VerdictColor(e.verdict));
            Label(x + 10f, y - 9f, e.verdict + "  aim " + e.aimDistance.ToString("0.0") + "/" + e.allowedRadius.ToString("0.0") + "m  dist " + e.distance.ToString("0") + "m");
        }

        // Parameter panel (top left).
        string panel =
            "HOMING SENSOR  (F3 to hide)\n" +
            "aim radius  near " + motor.homingAimRadius.ToString("0.00") + " [ ]   far " + motor.homingAimRadiusFar.ToString("0.00") + " ; '\n" +
            "range       " + motor.homingRange.ToString("0") + " m     - = to change\n" +
            "enemies in range: " + motor.HomingDebug.Count + "   chosen: " + (chosen > 0 ? "yes" : "none") + "\n" +
            "(copy these values into the Inspector before leaving play mode)";
        Box(new Rect(14f, 14f, 380f, 82f), new Color(0f, 0f, 0f, 0.65f));
        GUI.color = Color.white;
        GUI.Label(new Rect(20f, 16f, 370f, 80f), panel, smallStyle);
    }

    static Color VerdictColor(HomingVerdict v)
    {
        switch (v)
        {
            case HomingVerdict.Chosen:            return new Color(1f, 0.15f, 0.1f);
            case HomingVerdict.Valid:             return new Color(1f, 0.6f, 0.1f);
            case HomingVerdict.OutsideAimZone:    return new Color(0.65f, 0.65f, 0.65f);
            case HomingVerdict.BehindShadow:      return new Color(0.3f, 0.3f, 0.3f);
            case HomingVerdict.Blocked:           return new Color(1f, 0.9f, 0.2f);
            default:                              return new Color(0.7f, 0.3f, 1f); // lockout
        }
    }

    // ------------------------------------------------------------------
    // Small helpers
    void Label(float x, float y, string text)
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 3f, 320f, 20f), text, smallStyle);
    }

    static void DrawRing(Vector2 centre, float radius, Color c)
    {
        const int segments = 64;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Box(new Rect(centre.x + Mathf.Cos(a) * radius - 1f, centre.y + Mathf.Sin(a) * radius - 1f, 2f, 2f), c);
        }
    }

    static void Box(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}