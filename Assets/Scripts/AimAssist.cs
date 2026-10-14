using UnityEngine;

// ============================================================================
// AimAssist.cs                                                       (v0.1)
// GOES ON: nothing. It holds a data class + a static helper. Put it in Assets/Shadow/Scripts/.
//
// JOB: The ONE shared aim-assist toolkit.
//   * AimAssistProfile = the per-weapon tuning numbers (a plain serializable class, so every
//     weapon, and later every melee attack, carries its own copy in the Inspector).
//   * AimAssist        = the player's overall strength setting + FindBest(), the query that
//     answers "which enemy is closest to this aim line?". Bullet forgiveness, mouse friction
//     and (later) MELEE LOCK-ON all call the same function with different numbers.
//
// DESIGN RULES: assist never moves the camera, never turns a miss into a headshot, never
// shoots through walls, and always scales with the player's strength setting.
// ============================================================================
[System.Serializable]
public class AimAssistProfile
{
    public bool enabled = true;

    [Header("Hit forgiveness (a near-miss becomes a BODY hit)")]
    [Tooltip("Half-angle of the forgiveness cone at 100% player strength. 0 = off (shotguns).")]
    public float coneDegrees = 3f;
    [Tooltip("The cone never gets wider than this many metres, however far the target is.")]
    public float coneMaxRadius = 1.2f;

    [Header("Look friction (the mouse slows down over an enemy)")]
    [Range(0f, 1f)]
    [Tooltip("Sensitivity lost at the dead centre of an enemy, at 100% player strength. 0.5 = half speed.")]
    public float frictionAmount = 0.5f;
    public float frictionDegrees = 2.5f;   // how close to the reticle an enemy must be for friction
    public float frictionMaxRadius = 1.0f;
    public float frictionRange = 60f;
}

public static class AimAssist
{
    // ---- The player's setting: 0 = off, 1 = full. Default 0.5. Saved between sessions. ----
    // (Debug keys , and . change it for now. A settings menu will set it later.)
    const string PrefKey = "ShadowAimAssist";
    static float strength = -1f;
    public static float Strength
    {
        get
        {
            if (strength < 0f) strength = PlayerPrefs.GetFloat(PrefKey, 0.5f);
            return strength;
        }
        set
        {
            strength = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(PrefKey, strength);
        }
    }

    public struct Result
    {
        public IDamageable target;   // the enemy
        public Collider collider;    // the collider of it that is nearest the aim line
        public Vector3 point;        // the nearest point on that collider
        public float score;          // 0 = dead centre of the aim line, 1 = on the edge of the cone
    }

    static readonly Collider[] buf = new Collider[64];

    // Finds the enemy nearest the aim line (origin + dir * range) that is inside a cone of `degrees`
    // (never wider than `maxRadius` metres). Does NOT check walls: the caller does that, from wherever
    // makes sense for it (the gun for bullets, the camera for friction, the fists for melee).
    //   ignoreRoot = Shadow's transform (skipped)
    //   minDepth   = ignore anything closer than this along the aim line (stops enemies BEHIND Shadow counting)
    public static bool FindBest(Vector3 origin, Vector3 dir, float range, float degrees, float maxRadius,
                                LayerMask mask, Transform ignoreRoot, float minDepth, out Result best)
    {
        best = default;
        if (degrees <= 0f || maxRadius <= 0f || range <= 0f) return false;

        // Only colliders within maxRadius metres of the aim line can possibly qualify: one capsule query.
        int n = Physics.OverlapCapsuleNonAlloc(origin, origin + dir * range, maxRadius, buf, mask,
                                               QueryTriggerInteraction.Ignore);
        float tan = Mathf.Tan(degrees * Mathf.Deg2Rad);
        float bestScore = float.MaxValue;
        bool found = false;

        for (int i = 0; i < n; i++)
        {
            Collider col = buf[i];
            if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;

            IDamageable d = col.GetComponentInParent<IDamageable>();
            if (d == null) continue;

            // Nearest point of this collider to the aim line.
            Vector3 probe = origin + dir * Mathf.Clamp(Vector3.Dot(col.bounds.center - origin, dir), 0f, range);
            Vector3 p = ClosestOn(col, probe);

            float depth = Vector3.Dot(p - origin, dir);           // how far along the aim line
            if (depth < Mathf.Max(0.5f, minDepth)) continue;

            float miss = (p - (origin + dir * depth)).magnitude;  // metres off the line
            float allowed = Mathf.Min(maxRadius, depth * tan);    // angular cone, capped in metres
            if (allowed <= 0.0001f) continue;

            float score = miss / allowed;
            if (score > 1f || score >= bestScore) continue;

            bestScore = score;
            best = new Result { target = d, collider = col, point = p, score = score };
            found = true;
        }
        return found;
    }

    // Collider.ClosestPoint only works on simple convex colliders; fall back to the bounding box otherwise.
    static Vector3 ClosestOn(Collider col, Vector3 p)
    {
        if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider
            || (col is MeshCollider mc && mc.convex))
            return col.ClosestPoint(p);
        return col.bounds.ClosestPoint(p);
    }
}