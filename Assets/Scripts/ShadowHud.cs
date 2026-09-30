using UnityEngine;

// ============================================================================
// ShadowHud.cs                                                       (v0.3)
// GOES ON: Player (root). It finds ShadowMotor and ShadowTestWeapon on its own.
//
// JOB: Temporary HUD drawn with OnGUI so you need no Canvas yet:
//   * Crosshair at screen centre (this IS the aim point: the camera ray goes through it)
//   * Boost bar, bottom left (cyan; red when locked out)
//   * Ammo counters, bottom right
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

    ShadowMotor motor;
    ShadowTestWeapon weapon;   // optional
    GUIStyle ammoStyle;

    void Awake()
    {
        motor = GetComponent<ShadowMotor>();
        weapon = GetComponent<ShadowTestWeapon>();
    }

    void OnGUI()
    {
        DrawCrosshair();
        DrawBoostBar();
        DrawAmmo();
    }

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

    void DrawBoostBar()
    {
        Rect bg = new Rect(20f, Screen.height - 40f, 240f, 16f);
        Box(bg, new Color(0f, 0f, 0f, 0.6f));
        Box(new Rect(bg.x + 2f, bg.y + 2f, (bg.width - 4f) * motor.BoostNormalized, bg.height - 4f),
            motor.IsBoostLocked ? Color.red : Color.cyan);
    }

    void DrawAmmo()
    {
        if (weapon == null) return;
        if (ammoStyle == null)
            ammoStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.LowerRight };

        GUI.color = Color.white;
        string text = weapon.infiniteAmmo
            ? "AMMO  INF"
            : $"AMMO  {weapon.PrimaryAmmo}  |  SHELLS  {weapon.AltAmmo}";
        GUI.Label(new Rect(Screen.width - 320f, Screen.height - 44f, 300f, 28f), text, ammoStyle);
    }

    static void Box(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
