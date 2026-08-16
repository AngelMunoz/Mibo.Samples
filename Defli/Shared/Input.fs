namespace Defli

open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open Defli.State
open Defli.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// Input — the shared input wiring for the windowed frontends
// (Defli/Raylib + the MonoGame clients). Input arrives through the
// subscription projection (the IInput service delivers per-frame
// deltas); the handlers post intents that drain after Update,
// before the frame force. Shell state stays direct; a restart
// swaps the state cell in place.
//
// Ordering per frame (host): input.Poll() → the subscriptions run
// (shell writes + posted intents) → Application.update → intents drained
// → frame forced → draw. Keyboard pan deltas post as camera
// messages (AddKeyboardPan) and decay inside Camera.tick.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type GameAction =
  | StartNextWave
  | ToggleDiagnostics
  | SelectArrow
  | SelectFrost
  | SelectCannon
  | Restart
  | ResetCamera
  | PanLeft
  | PanRight
  | PanUp
  | PanDown

module Inputs =

  /// The key bindings — the original InputMap as a plain match (the
  /// mapper's ActionState tracking is replaced by the camera's
  /// keyboard-pan accumulation, CameraMsg.AddKeyboardPan).
  let actionOfKey(key: KeyCode) : GameAction voption =
    match key with
    | KeyCode.Space
    | KeyCode.Enter -> ValueSome GameAction.StartNextWave
    | KeyCode.F3 -> ValueSome GameAction.ToggleDiagnostics
    | KeyCode.D1 -> ValueSome GameAction.SelectArrow
    | KeyCode.D2 -> ValueSome GameAction.SelectFrost
    | KeyCode.D3 -> ValueSome GameAction.SelectCannon
    | KeyCode.R -> ValueSome GameAction.Restart
    | KeyCode.Home -> ValueSome GameAction.ResetCamera
    | KeyCode.A
    | KeyCode.Left -> ValueSome GameAction.PanLeft
    | KeyCode.D
    | KeyCode.Right -> ValueSome GameAction.PanRight
    | KeyCode.W
    | KeyCode.Up -> ValueSome GameAction.PanUp
    | KeyCode.S
    | KeyCode.Down -> ValueSome GameAction.PanDown
    | _ -> ValueNone

  /// The pan direction a pan action contributes. The pressed key moves
  /// the CAMERA (Up → the view pans north); Camera.Pan subtracts its
  /// input (drag semantics: the world follows the cursor), so keyboard
  /// deltas carry the OPPOSITE sign of the drag they mirror.
  let inline panStep(action: GameAction) : Vector2 =
    match action with
    | GameAction.PanLeft -> Vector2(1f, 0f)
    | GameAction.PanRight -> Vector2(-1f, 0f)
    | GameAction.PanUp -> Vector2(0f, 1f)
    | GameAction.PanDown -> Vector2(0f, -1f)
    | _ -> Vector2.Zero

module Input =

  /// Screen → world → the CONTAINING cell (the sim's cellAt — the
  /// floor-based pick, not the nearest-center one). Picking goes
  /// through the CLAMPED camera (the same composition CameraView
  /// uses for the drawn camera) so the cursor maps to the cells the
  /// player actually sees.
  let private hoverCell
    (state: State)
    (viewport: Vector2)
    (screenPos: Vector2)
    : struct (int * int) voption =
    let worldPos =
      Camera.screenToWorld
        (Camera.clampToWorld state.Camera.State viewport)
        viewport
        screenPos

    Defli.Application.cellAt
      worldPos
      (Defli.State.Systems.MapModel.terrain state.Map)

  let private handleKeyboard
    (post: (unit -> unit) -> unit)
    (cell: StateCell)
    (shell: Shell)
    (delta: KeyboardDelta)
    : unit =
    for code in delta.Pressed do
      match Inputs.actionOfKey code with
      | ValueSome action ->
        match action with
        | GameAction.StartNextWave ->
          post(fun () -> Application.startNextWave cell.Value)
        | GameAction.ToggleDiagnostics ->
          shell.Diag.Visible <- not shell.Diag.Visible
        | GameAction.SelectArrow ->
          post(fun () -> Application.selectTower cell.Value TowerDefs.arrow)
        | GameAction.SelectFrost ->
          post(fun () -> Application.selectTower cell.Value TowerDefs.frost)
        | GameAction.SelectCannon ->
          post(fun () -> Application.selectTower cell.Value TowerDefs.cannon)
        | GameAction.ResetCamera ->
          post(fun () -> Camera.handle CameraMsg.Reset cell.Value.Camera)
        | GameAction.Restart ->
          // Restart stays in the game logic: swap the state inside an
          // intent. The frame force reads the cell at force time, so
          // the next force re-binds the graph to the fresh state — no
          // window/runner re-create.
          if AVal.getValue cell.Value.Economy.GameOver then
            post(fun () ->
              cell.Value <- State.init cell.Value.Config
              shell.MiddleDown <- false)
        | GameAction.PanLeft
        | GameAction.PanRight
        | GameAction.PanUp
        | GameAction.PanDown ->
          post(fun () ->
            Camera.handle
              (CameraMsg.AddKeyboardPan(Inputs.panStep action))
              cell.Value.Camera)
      | ValueNone -> ()

    for code in delta.Released do
      match Inputs.actionOfKey code with
      | ValueSome action ->
        match action with
        | GameAction.PanLeft
        | GameAction.PanRight
        | GameAction.PanUp
        | GameAction.PanDown ->
          // Released subtracts what the press added — the accumulated
          // keyboard pan decays back to zero.
          post(fun () ->
            Camera.handle
              (CameraMsg.AddKeyboardPan(-Inputs.panStep action))
              cell.Value.Camera)
        | _ -> ()
      | ValueNone -> ()

  let private handleMouse
    (post: (unit -> unit) -> unit)
    (wheelScale: float)
    (ctx: AdaptiveFrameContext)
    (cell: StateCell)
    (shell: Shell)
    (delta: MouseDelta)
    : unit =
    let state = cell.Value

    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Middle then
      shell.MiddleDown <- true

    if delta.Buttons.Released |> Array.contains MouseButtonCode.Middle then
      shell.MiddleDown <- false

    let viewport = Vector2(float32 ctx.WindowWidth, float32 ctx.WindowHeight)

    // Hover cell — the CVal the state projections join on. Direct root
    // write — the poll runs on the game thread before Step.
    state.HoverCell |> CVal.set(hoverCell state viewport delta.Position)

    // Wheel zoom / middle-drag pan: posted camera messages (no dt).
    if delta.ScrollDelta <> 0f then
      // Multiplicative steps toward the camera target. wheelScale is
      // the zoom factor per unit of ScrollDelta: the raylib client
      // reports ±1 per notch (pass 1.1); MonoGame reports the raw XNA
      // wheel (±120 per notch), so its client passes the per-notch
      // base `1.1 ** (1.0 / 120.0)` to keep the same feel.
      let factor = float32(wheelScale ** float delta.ScrollDelta)

      post(fun () -> Camera.handle (CameraMsg.ZoomBy factor) cell.Value.Camera)
    elif shell.MiddleDown && delta.PositionDelta <> Vector2.Zero then
      // Middle-drag pan: world moves opposite the drag (screen px).
      post(fun () ->
        Camera.handle (CameraMsg.Pan delta.PositionDelta) cell.Value.Camera)

    // Clicks → place / upgrade (Application validates everything).
    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
      hoverCell state viewport delta.Position
      |> ValueOption.iter(fun c ->
        post(fun () -> Application.placeTower cell.Value c |> ignore))

    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Right then
      hoverCell state viewport delta.Position
      |> ValueOption.iter(fun c ->
        post(fun () -> Application.upgradeTower cell.Value c |> ignore))

  /// The two input subscriptions (keyboard + mouse) the windowed
  /// frontends wire via
  /// `AdaptiveInit.withSubscriptions (Input.subscriptions wheelScale cell shell)`.
  /// wheelScale is the wheel-zoom factor per ScrollDelta unit — see
  /// handleMouse.
  let subscriptions
    (wheelScale: float)
    (cell: StateCell)
    (shell: Shell)
    (frameCtx: AdaptiveFrameContext)
    : amap<SubId, AdaptiveSub> =
    let input = frameCtx.Context |> GameContext.getService<IInput>

    AMap.ofList [
      SubId.ofString "keyboard",
      AdaptiveSub.ofObservable
        (SubId.ofString "keyboard")
        input.KeyboardDelta
        (fun posting -> handleKeyboard posting.Post cell shell)

      SubId.ofString "mouse",
      AdaptiveSub.ofObservable
        (SubId.ofString "mouse")
        input.MouseDelta
        (fun posting -> handleMouse posting.Post wheelScale frameCtx cell shell)
    ]
