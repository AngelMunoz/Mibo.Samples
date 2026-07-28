module AnimatedInstancing.MonoGame.Types

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Input
open AnimatedInstancing

/// Everything the Crowd sub-system needs to (re)build per-instance states:
/// the renderable content-pipeline model, the raw-loaded skeleton, and the
/// merged clip set.
type Rig = {
  Model: Microsoft.Xna.Framework.Graphics.Model
  Mesh: AnimatedMesh voption
  Clips: Animation3DClips
}

/// Crowd sub-system model: one playback state per live instance, a static
/// transform per instance, and a reused pose array the View fills once per
/// Draw. Arrays are sized to the current tier and rebuilt on tier change —
/// never allocated per frame.
type CrowdModel = {
  TierIndex: int
  Count: int
  Paused: bool
  /// Auto-orbit angle (radians); advanced every Tick, paused or not.
  CameraAngle: float32
  States: Animation3DState[]
  Transforms: Matrix[]
  Poses: BonePose[]
}

module CrowdModel =
  let empty: CrowdModel = {
    TierIndex = 0
    Count = 0
    Paused = false
    CameraAngle = 0.0f
    States = [||]
    Transforms = [||]
    Poses = [||]
  }

// -------------------------------------------------------------
// Root Model — composes sub-system models + backend-specific state
// -------------------------------------------------------------

type Model() =
  // Sub-system models
  member val Crowd = CrowdModel.empty with get, set
  member val Diag = Diagnostics.init() with get, set
  // Input
  member val Actions: ActionState<GameAction> = ActionState.empty with get, set
  member val InputMap: InputMap<GameAction> = InputMap.empty with get, set
  // Backend-specific state
  member val Rig = Unchecked.defaultof<Rig> with get, set
  member val GroundMesh = Unchecked.defaultof<PrimitiveMesh> with get, set
  member val DiagFont = Unchecked.defaultof<SpriteFont> with get, set

// -------------------------------------------------------------
// Root Msg
// -------------------------------------------------------------

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputMapped of inputs: ActionState<GameAction>
