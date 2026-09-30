using UnityEngine;

// ============================================================================
// ShadowInput.cs                                                     (v0.3)
// GOES ON: Player (root object, next to CharacterController + ShadowMotor)
//
// JOB: The ONLY script that talks to the Input System. Everything else
// (motor, camera, weapon) reads from here.
//
// REQUIRES: your Input Actions asset with "Generate C# Class" ticked. Your
// generated class is named `InputSystem_Actions`, and its "Gameplay" map must
// contain: Move, Look, Jump, Boost, Fire, AltFire, Dodge, GroundSlam.
// (If you ever rename the asset, change the class name on the line below.)
// ============================================================================
public class ShadowInput : MonoBehaviour
{
    InputSystem_Actions controls; // auto-generated from your .inputactions asset

    // ---- Values other scripts read ----
    // x = right(+)/left(-), y = forward(+)/back(-).
    public Vector2 Move => controls.Gameplay.Move.ReadValue<Vector2>();

    // Raw mouse delta for this frame. Do NOT multiply by deltaTime.
    public Vector2 Look => controls.Gameplay.Look.ReadValue<Vector2>();

    public bool JumpPressed   => controls.Gameplay.Jump.WasPressedThisFrame();       // one frame
    public bool DodgePressed  => controls.Gameplay.Dodge.WasPressedThisFrame();      // one frame
    public bool SlamPressed   => controls.Gameplay.GroundSlam.WasPressedThisFrame(); // one frame
    public bool BoostHeld     => controls.Gameplay.Boost.IsPressed();                // while held
    public bool FireHeld      => controls.Gameplay.Fire.IsPressed();                 // while held
    public bool AltFireHeld   => controls.Gameplay.AltFire.IsPressed();              // while held

    void Awake()     => controls = new InputSystem_Actions();
    void OnEnable()  => controls.Gameplay.Enable();   // start listening
    void OnDisable() => controls.Gameplay.Disable();  // stop listening
    void OnDestroy() => controls.Dispose();
}