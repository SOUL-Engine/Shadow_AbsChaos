using UnityEngine;

// ============================================================================
// ShadowInput.cs
// GOES ON: Player (the root object, next to CharacterController + ShadowMotor)
//
// JOB: The ONLY script that talks to the Input System. Everything else
// (motor, camera, later weapons) reads from here. That means when you add a
// gamepad or a rebinding menu, this is the only file you touch.
//
// REQUIRES: ShadowControls.inputactions with "Generate C# Class" ticked.
//           That generates the `ShadowControls` class used below.
// ============================================================================
public class ShadowInput : MonoBehaviour
{
    InputSystem_Actions controls; // auto-generated from InputSystem_Actions.inputactions

    // ---- Values other scripts read ----
    // x = right(+)/left(-), y = forward(+)/back(-). Already normalised for diagonals.
    public Vector2 Move => controls.Gameplay.Move.ReadValue<Vector2>();

    // Raw mouse delta for this frame (pixels). Do NOT multiply by deltaTime.
    public Vector2 Look => controls.Gameplay.Look.ReadValue<Vector2>();

    public bool JumpPressed => controls.Gameplay.Jump.WasPressedThisFrame(); // true for one frame
    public bool BoostHeld   => controls.Gameplay.Boost.IsPressed();          // true while held

    // Reserved for later systems (weapons, dodge). Declared now so the asset
    // and this wrapper don't need editing when those systems arrive.
    public bool FireHeld        => controls.Gameplay.Fire.IsPressed();
    public bool AltFireHeld     => controls.Gameplay.AltFire.IsPressed();
    public bool DodgePressed    => controls.Gameplay.Dodge.WasPressedThisFrame();

    void Awake()     => controls = new InputSystem_Actions();
    void OnEnable()  => controls.Gameplay.Enable();   // start listening
    void OnDisable() => controls.Gameplay.Disable();  // stop listening
    void OnDestroy() => controls.Dispose();
}