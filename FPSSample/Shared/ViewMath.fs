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

  /// Enemy transform components: facing rotation angle and position.
  /// The backend builds its own matrix from these (Raymath vs Matrix.Create).
  let inline enemyTransform(enemy: Enemy) : struct (float32 * Vector3) =
    struct (enemy.Facing, enemy.Position)

  /// Weapon viewmodel position relative to player. Adds pitch and recoil offsets
  /// so the nozzle tracks the crosshair and kicks back after each shot.
  let inline weaponPosition
    (playerPos: Vector3)
    (forward: Vector3)
    (pitch: float32)
    (yaw: float32)
    (recoil: float32)
    : Vector3 =
    let right = cameraRight yaw

    let basePos =
      playerPos + forward * 0.8f + right * 0.4f + Vector3(0.0f, -0.3f, 0.0f)
    // Pull the weapon back and down slightly with pitch so the nozzle stays
    // aligned with the camera look direction.
    let pitchOffset = -MathF.Sin(pitch) * 0.15f
    let recoilOffset = -recoil
    basePos + Vector3(0.0f, pitchOffset, recoilOffset)

  /// Muzzle flash light position.
  let inline muzzleFlashPosition
    (playerPos: Vector3)
    (forward: Vector3)
    : Vector3 =
    playerPos + forward * 0.6f

  /// World-space position of the gun nozzle. This matches the visual viewmodel
  /// so effects like smoke and the muzzle flash spawn at the actual barrel exit.
  let inline muzzleWorldPosition
    (playerPos: Vector3)
    (forward: Vector3)
    (pitch: float32)
    (yaw: float32)
    : Vector3 =
    weaponPosition playerPos forward pitch yaw 0.0f + forward * 0.55f

  // ── Scene lighting constants (identical across backends) ─────────────────────
  // Night-time atmosphere: dim moonlit ambient, cool moonlight directional.

  /// Ambient light for the FPS scene (dim moonlit blue).
  let ambientLight: AmbientLight3D = {
    Color = Mibo.Color.rgb 20uy 25uy 45uy
    Intensity = 0.12f
  }

  /// Directional light posing as the moon (cool, low intensity).
  let directionalLight: DirectionalLight3D = {
    Direction = Vector3.Normalize(Vector3(-0.4f, -1.0f, 0.3f))
    Color = Mibo.Color.rgb 150uy 170uy 230uy
    Intensity = 0.35f
    CastsShadows = true
  }

  /// Sky clear color (dark night sky).
  let clearColor: Mibo.Color = Mibo.Color.rgb 8uy 10uy 22uy

  /// Horizon tint for the procedural starry sky.
  let skyHorizonColor: Mibo.Color = Mibo.Color.rgb 15uy 18uy 35uy

  /// Zenith tint for the procedural starry sky.
  let skyZenithColor: Mibo.Color = Mibo.Color.rgb 5uy 7uy 18uy

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

  // ── Torch (point light) positions ───────────────────────────────────────────
  // Static torches scattered around the arena for atmosphere. Placed near
  // cover, crates, and corners so the player has lit areas to navigate by.

  /// Static torch positions in world space (placed around the arena).
  let torchPositions: Vector3[] = [|
    Vector3(-8.0f, 1.5f, -8.0f) // near health pickup (NW)
    Vector3(8.0f, 1.5f, -4.0f) // near health pickup (NE)
    Vector3(0.0f, 1.5f, 0.0f) // central pillar area
    Vector3(-12.0f, 1.5f, 5.0f) // near enemy spawn (SW)
    Vector3(10.0f, 1.5f, 8.0f) // SE corner
  |]

  /// Creates a warm torch point light at the given position, with a flicker
  /// offset applied to intensity (caller passes a per-torch phase so each
  /// torch flickers independently).
  let torchLight (pos: Vector3) (flicker: float32) : PointLight3D = {
    Position = pos
    Color = Mibo.Color.rgb 255uy 160uy 60uy
    Intensity = 2.0f + flicker
    Radius = 7.0f
    Falloff = 1.8f
    CastsShadows = false
    ShadowBias = ValueNone
  }
