module AnimatedInstancing.Raylib.Types

open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Animation
open Mibo.Input
open AnimatedInstancing

/// Everything the Crowd sub-system needs to (re)build per-instance states:
/// the loaded rig model and the merged clip set.
type Rig = {
  Model: Raylib_cs.Model
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
  Transforms: Matrix4x4[]
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
  member val AnimMesh: AnimatedMesh voption = ValueNone with get, set
  member val GroundMesh = Unchecked.defaultof<Mesh> with get, set
  member val DiagFont = Raylib.GetFontDefault() with get, set
  /// Whether the directional light casts shadows (S toggles; the shadow pass
  /// is the biggest per-frame cost at high crowd tiers).
  member val ShadowsOn = true with get, set

// -------------------------------------------------------------
// Root Msg
// -------------------------------------------------------------

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputMapped of inputs: ActionState<GameAction>
