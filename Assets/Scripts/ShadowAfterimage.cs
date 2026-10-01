using UnityEngine;

// ============================================================================
// ShadowAfterimage.cs                                                (v0.4)
// GOES ON: nothing. It is created at runtime by ShadowMotor via Spawn().
//          Just put this file in Assets/Shadow/Scripts/.
//
// JOB: Leaves a fading, flat-coloured copy of Shadow's Visual in the world.
// The Chaos dodge uses these to sell "he was HERE, now he's THERE".
//
// NOTE: uses the built-in "Sprites/Default" shader (flat colour + alpha, works
// in URP and Built-in). If you make a standalone BUILD and the ghosts come out
// pink, add it under Project Settings > Graphics > Always Included Shaders.
// ============================================================================
public class ShadowAfterimage : MonoBehaviour
{
    static Shader ghostShader;

    Material mat;
    Color startColor;
    float life, age, endScale;
    Vector3 startScale;

    // source   = the Visual transform to copy
    // color    = tint (alpha = starting opacity)
    // life     = seconds until fully faded
    // endScale = 1 keeps the size; above 1 makes the ghost swell as it fades (used for the arrival flash)
    public static void Spawn(Transform source, Color color, float life, float endScale = 1f)
    {
        if (ghostShader == null) ghostShader = Shader.Find("Sprites/Default");

        // Clone the whole Visual (capsule + face + arm) at its current pose, with NO parent.
        GameObject clone = Instantiate(source.gameObject, source.position, source.rotation);
        clone.name = "Afterimage";
        clone.transform.localScale = source.lossyScale;

        // The clone must be a FROZEN, visual-only statue of Shadow's current pose:
        //  * Colliders would block things.
        //  * An Animator would keep animating the copy (a ghost that runs on the spot).
        //  * Scripts would run a second copy of their logic.
        // Bones are plain Transforms, so the pose is already copied; removing the Animator freezes it.
        foreach (Collider c in clone.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (Animator a in clone.GetComponentsInChildren<Animator>()) Destroy(a);
        foreach (MonoBehaviour m in clone.GetComponentsInChildren<MonoBehaviour>()) Destroy(m);

        Material mat = new Material(ghostShader);
        foreach (Renderer r in clone.GetComponentsInChildren<Renderer>())
        {
            r.enabled = true; // the real Visual is hidden mid-warp, so force the copy visible
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        ShadowAfterimage g = clone.AddComponent<ShadowAfterimage>();
        g.mat = mat;
        g.startColor = color;
        g.life = Mathf.Max(0.01f, life);
        g.endScale = endScale;
        g.startScale = clone.transform.localScale;
        mat.color = color;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / life);

        Color c = startColor;
        c.a *= (1f - t) * (1f - t);   // quadratic fade: bright at first, then gone quickly
        mat.color = c;

        transform.localScale = Vector3.Lerp(startScale, startScale * endScale, t);

        if (t >= 1f) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}