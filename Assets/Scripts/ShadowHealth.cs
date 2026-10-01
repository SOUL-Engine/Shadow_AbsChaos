using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================================
// ShadowHealth.cs                                                    (v0.8)
// GOES ON: Player (root object, next to ShadowMotor).
//
// JOB: Shadow's health. The rules, straight from the design pillars:
//   * NO REGENERATION. Every hit is permanent until a pickup heals you.
//   * It implements IDamageable, so enemy shots and hazards hurt Shadow through the
//     same one-line contract everything else uses.
//   * It asks the motor first: ShadowMotor.IsInvulnerable is true during the Chaos
//     warp (and optionally spin dash / ball dash). Damage during that window is ignored.
//   * A tiny `hitGrace` window after each hit stops one shotgun blast landing every
//     pellet as separate damage.
//   * Shadow does not flinch. Taking a hit never interrupts his movement
//     (movement is a weapon: being punished twice for one hit is not fair).
//
// DEATH: the movement and weapon switch off, the HUD shows YOU DIED, and after
// `restartDelay` the scene reloads. (A scene reload only works if the scene is in
// File > Build Profiles / Build Settings: use "Add Open Scenes". If it isn't, he
// respawns in place instead, which resets Shadow but not the enemies.)
// ============================================================================
[RequireComponent(typeof(ShadowMotor))]
public class ShadowHealth : MonoBehaviour, IDamageable
{
    [Header("Health (no regeneration: only pickups heal)")]
    public float maxHealth = 100f;
    public float hitGrace = 0.15f;      // seconds after a hit during which further damage is ignored

    [Header("Death")]
    public float restartDelay = 2f;
    public bool reloadSceneOnDeath = true;

    // ---- Read by the HUD and pickups ----
    public float Health { get; private set; }
    public float Health01 => Health / maxHealth;
    public bool IsFull => Health >= maxHealth - 0.01f;
    public bool IsDead { get; private set; }
    public float LastDamageTime { get; private set; } = -10f;
    public float LastDamageAmount { get; private set; }
    public float LastHealTime { get; private set; } = -10f;

    public event System.Action<float> Damaged; // amount
    public event System.Action Died;

    ShadowMotor motor;
    ShadowTestWeapon weapon;   // optional
    float graceUntil;
    Vector3 spawnPos;

    void Awake()
    {
        motor = GetComponent<ShadowMotor>();
        weapon = GetComponent<ShadowTestWeapon>();
        Health = maxHealth;
        spawnPos = transform.position;
    }

    // IDamageable. Enemy shots, projectiles and hazards all call this.
    public void TakeDamage(float amount, Vector3 point)
    {
        if (IsDead || amount <= 0f) return;
        if (motor.IsInvulnerable) return;       // Chaos warp: untouchable
        if (Time.time < graceUntil) return;     // just got hit

        Health = Mathf.Max(0f, Health - amount);
        graceUntil = Time.time + hitGrace;
        LastDamageTime = Time.time;
        LastDamageAmount = amount;
        Damaged?.Invoke(amount);

        if (Health <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Health = Mathf.Min(maxHealth, Health + amount);
        LastHealTime = Time.time;
    }

    void Die()
    {
        IsDead = true;
        Died?.Invoke();
        motor.enabled = false;                  // stops all movement input
        if (weapon != null) weapon.enabled = false;
        Invoke(nameof(Restart), restartDelay);
    }

    void Restart()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (reloadSceneOnDeath && Application.CanStreamedLevelBeLoaded(scene))
        {
            SceneManager.LoadScene(scene);
            return;
        }

        // Fallback: respawn in place (scene isn't in Build Settings, or reload is off).
        CharacterController cc = GetComponent<CharacterController>();
        cc.enabled = false;                     // a CharacterController must be off to be teleported
        transform.position = spawnPos;
        cc.enabled = true;

        Health = maxHealth;
        IsDead = false;
        graceUntil = 0f;
        motor.enabled = true;
        motor.ResetMotion();
        if (weapon != null) weapon.enabled = true;
    }
}
