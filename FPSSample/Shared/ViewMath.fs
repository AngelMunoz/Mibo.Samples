namespace FPSSample

open System
open System.Numerics
open Mibo.Elmish.Graphics3D
open FPSSample.Types

/// <summary>
/// Shared view math and scene setup constants, used by all backend views.
/// These are pure computations on System.Numerics types — no backend dependency.
/// Each backend's view function calls these helpers then does its own
/// platform-specific camera construction and Draw3D calls.
/// </summary>
module ViewMath =

  /// Camera forward vector from player yaw/pitch (FPS first-person look).
  let inline cameraForward (yaw: float32) (pitch: float32) : Vector3 =
    let cosP = MathF.Cos(pitch)
    Vector3(-MathF.Sin(yaw) * cosP, MathF.Sin(pitch), -MathF.Cos(yaw) * cosP)

  /// Camera right vector from player yaw (horizontal only).
  let inline cameraRight(yaw: float32) : Vector3 =
    Vector3(MathF.Cos(yaw), 0.0f, -MathF.Sin(yaw))

  /// Pickup bobbing offset at a given total time.
  let inline pickupBob(totalTime: float32) : float32 =
    MathF.Sin(totalTime * 3.0f) * 0.2f

  /// Tracer travel progress [0..1] based on remaining timer.
  let inline tracerProgress(tracer: Tracer) : float32 =
    1.0f - tracer.Timer / Tracer.duration

  /// Tracer alpha for the faint line overlay.
  let inline tracerAlpha(tracer: Tracer) : float32 =
    MathF.Max(0.0f, tracer.Timer / Tracer.duration) * 0.5f

  /// Enemy transform components: facing rotation angle and position.
  /// The backend builds its own matrix from these (Raymath vs Matrix.Create).
  let inline enemyTransform(enemy: Enemy) : struct (float32 * Vector3) =
    struct (enemy.Facing, enemy.Position)

  /// Weapon viewmodel position relative to player.
  let inline weaponPosition
    (playerPos: Vector3)
    (forward: Vector3)
    (yaw: float32)
    : Vector3 =
    let right = cameraRight yaw
    playerPos + forward * 0.8f + right * 0.4f + Vector3(0.0f, -0.3f, 0.0f)

  /// Muzzle flash light position.
  let inline muzzleFlashPosition
    (playerPos: Vector3)
    (forward: Vector3)
    : Vector3 =
    playerPos + forward * 0.5f

  // ── Scene lighting constants (identical across backends) ─────────────────────

  /// Ambient light for the FPS scene.
  let ambientLight: AmbientLight3D = {
    Color = Mibo.Color.rgb 60uy 60uy 80uy
    Intensity = 0.4f
  }

  /// Directional light for the FPS scene.
  let directionalLight: DirectionalLight3D = {
    Direction = Vector3.Normalize(Vector3(0.3f, -1.0f, 0.2f))
    Color = Mibo.Color.rgb 255uy 245uy 220uy
    Intensity = 0.8f
    CastsShadows = true
  }

  /// Sky clear color.
  let clearColor: Mibo.Color = Mibo.Color.rgb 135uy 180uy 220uy

  /// Creates a muzzle flash point light at the given position.
  let muzzleFlashLight(pos: Vector3) : PointLight3D = {
    Position = pos
    Color = Mibo.Color.rgb 255uy 220uy 120uy
    Intensity = 3.0f
    Radius = 5.0f
    Falloff = 2.0f
    CastsShadows = false
    ShadowBias = ValueNone
  }
