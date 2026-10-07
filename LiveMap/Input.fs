module LiveMap.Input

open System.Numerics
open LiveMap
open Mibo.Elmish
open Mibo.Input
open Raylib_cs

/// The keys LiveMap binds.
///
/// Arrows pan, Q and E turn the 3D view, `+` and `-` zoom, Home re-frames
/// the map, 1 and 2 pick the map, Tab swaps the document syntax, F1 hides
/// the help text, F11 goes fullscreen, Escape quits.
///
/// No mouse button is bound to anything: the mouse is the hover pointer
/// only. Dragging to pan is deliberately absent.
[<Struct>]
type GameAction =
  | PanLeft
  | PanRight
  | PanUp
  | PanDown
  | RotateLeft
  | RotateRight
  | ZoomIn
  | ZoomOut
  | ResetView
  | FlatMode
  | BlocksMode
  | SwapSyntax
  | ToggleHelp
  | ToggleFullScreen
  | Quit

type Model = { State: ActionState<GameAction> }

[<Struct>]
type Msg = InputChanged of state: ActionState<GameAction>

/// The intent one frame of held keys adds up to: a direction (one unit
/// per axis, +Y is up-screen), a zoom direction (+1 zooms in), and a
/// rotate direction (+1 turns the 3D view right).
[<Struct>]
type Intent = {
  Pan: Vector2
  Zoom: float32
  Rotate: float32
}

let inputMap: InputMap<GameAction> =
  InputMap.empty
  |> InputMap.key PanLeft KeyCode.Left
  |> InputMap.key PanLeft KeyCode.A
  |> InputMap.key PanRight KeyCode.Right
  |> InputMap.key PanRight KeyCode.D
  |> InputMap.key PanUp KeyCode.Up
  |> InputMap.key PanUp KeyCode.W
  |> InputMap.key PanDown KeyCode.Down
  |> InputMap.key PanDown KeyCode.S
  |> InputMap.key RotateLeft KeyCode.Q
  |> InputMap.key RotateRight KeyCode.E
  |> InputMap.key ZoomIn KeyCode.Equal
  |> InputMap.key ZoomIn KeyCode.KpAdd
  |> InputMap.key ZoomOut KeyCode.Minus
  |> InputMap.key ZoomOut KeyCode.KpSubtract
  |> InputMap.key ResetView KeyCode.Home
  |> InputMap.key FlatMode KeyCode.D1
  |> InputMap.key BlocksMode KeyCode.D2
  |> InputMap.key SwapSyntax KeyCode.Tab
  |> InputMap.key ToggleHelp KeyCode.F1
  |> InputMap.key ToggleFullScreen KeyCode.F11
  |> InputMap.key Quit KeyCode.Escape

let init: Model = { State = ActionState.empty }

/// The held pan, zoom, and rotate of one frame. Held keys are read as
/// state, not as edges, so a key that is down when the window gains focus
/// still pans.
let intent(model: Model) : Intent =
  let held = model.State.Held

  let axis (positive: GameAction) (negative: GameAction) =
    (if held.Contains positive then 1.0f else 0.0f)
    - (if held.Contains negative then 1.0f else 0.0f)

  {
    Pan = Vector2(axis PanRight PanLeft, axis PanUp PanDown)
    Zoom = axis ZoomIn ZoomOut
    Rotate = axis RotateRight RotateLeft
  }

/// Applies one input frame. The one-shot keys act here and emit nothing:
/// the router reads the model to decide what changed.
let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputChanged state ->
    if state.Started.Contains ToggleFullScreen then
      Raylib.ToggleBorderlessWindowed()

    struct ({ State = state }, Cmd.none)
