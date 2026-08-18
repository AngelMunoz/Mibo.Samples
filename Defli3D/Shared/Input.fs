namespace Defli3D

open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open Mibo.Windowing
open Defli3D.State
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// Input — the shared input wiring for the windowed frontends
// (Defli3D/Raylib + the MonoGame clients).
//
// KEYBOARD is semantic: the InputMapper subscription evaluates the
// GameAction InputMap (Domain.fs) against the input deltas and
// writes the Actions root through the pre-step lane; Application's
// update consumes the Started/Released edges (one-shots, pan
// accumulation, restart). No per-key handlers, no key codes here.
//
// POINTER is direct: hover picking goes through the 3D orbit camera
// (the mouse subscription has the viewport size and the camera
// state, and Camera.pickGroundCell unprojects the cursor to the
// ground plane); middle-drag pan posts Camera.pan with the raw
// pixel delta; the wheel posts ZoomBy; clicks post place/upgrade.
// F3 (diagnostics overlay) is frontend state — a direct subscription,
// not a GameAction.
//
// Ordering per frame (host): input.Poll() → the mapper builds and
// the pointer handlers post → Application.update (consumes action
// edges first) → intents drained → frame forced → draw.
// ─────────────────────────────────────────────────────────────

module Input =

  /// Screen → the CONTAINING ground cell through the 3D camera: the
  /// cursor is unprojected to the y=0 plane and floored (the same
  /// floor-based convention as the sim's cellAt — the containing
  /// tile, not the nearest-center one). Bounds-checked by the camera
  /// against the grid extent (WorldSize).
  let private hoverCell
    (state: State)
    (viewport: Vector2)
    (screenPos: Vector2)
    : struct (int * int) voption =
    Camera.pickGroundCell viewport.X viewport.Y screenPos state.Camera.State

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

    // Wheel zoom / middle-drag pan: posted camera calls (no dt).
    if delta.ScrollDelta <> 0f then
      // Multiplicative steps toward the camera target. wheelScale is
      // the zoom factor per unit of ScrollDelta: the raylib client
      // reports ±1 per notch (pass 1.1); MonoGame reports the raw XNA
      // wheel (±120 per notch), so its client passes the per-notch
      // base `1.1 ** (1.0 / 120.0)` to keep the same feel.
      let factor = float32(wheelScale ** float delta.ScrollDelta)

      post(fun () -> Camera.zoomBy factor cell.Value.Camera)
    elif shell.MiddleDown && delta.PositionDelta <> Vector2.Zero then
      // Middle-drag pan: world moves opposite the drag (screen px —
      // the camera converts to a yaw-relative XZ offset).
      post(fun () ->
        Camera.pan
          delta.PositionDelta.X
          delta.PositionDelta.Y
          cell.Value.Camera)

    // Clicks → place / upgrade (Application validates everything).
    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
      hoverCell state viewport delta.Position
      |> ValueOption.iter(fun c ->
        post(fun () -> Application.placeTower cell.Value c |> ignore))

    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Right then
      hoverCell state viewport delta.Position
      |> ValueOption.iter(fun c ->
        post(fun () -> Application.upgradeTower cell.Value c |> ignore))

  /// The input subscriptions the windowed frontends wire via
  /// `AdaptiveInit.withSubscriptions (Input.subscriptions wheelScale actionsSub cell shell)`:
  /// the semantic keyboard mapper (`actionsSub` — the BACKEND builds it,
  /// `InputMapper.subscribeStaticAdaptive Inputs.inputMap state.Actions ctx`;
  /// the factory lives in the backend packages, so Shared stays
  /// backend-free), the pointer handler, and the F3 diagnostics toggle.
  ///
  /// The returned projection caches its map and rebuilds only when the
  /// cell swaps (restart) so the mapper writes the fresh state's root —
  /// the runner's version check makes clean steps diff-free.
  let subscriptions
    (wheelScale: float)
    (actionsSub: GameContext -> AdaptiveSub)
    (cell: StateCell)
    (shell: Shell)
    : AdaptiveFrameContext -> amap<SubId, AdaptiveSub> =
    let mutable cached: amap<SubId, AdaptiveSub> = Unchecked.defaultof<_>
    let mutable cachedFor: State = Unchecked.defaultof<_>

    fun frameCtx ->
      let state = cell.Value

      if not(obj.ReferenceEquals(state, cachedFor)) then
        let input = frameCtx.Context |> GameContext.getService<IInput>

        cachedFor <- state

        cached <-
          AMap.ofList [
            SubId.ofString "actions", actionsSub frameCtx.Context

            SubId.ofString "mouse",
            AdaptiveSub.ofObservable
              (SubId.ofString "mouse")
              input.MouseDelta
              (fun posting ->
                handleMouse posting.Post wheelScale frameCtx cell shell)

            SubId.ofString "diagnostics",
            AdaptiveSub.ofObservable
              (SubId.ofString "diagnostics")
              input.KeyboardDelta
              (fun _ d ->
                if d.Pressed |> Array.contains KeyCode.F3 then
                  shell.Diag.Visible <- not shell.Diag.Visible)

            // F11 fullscreen: frontend state like F3, so a direct
            // subscription, not a GameAction. The host-registered IWindow
            // does the native toggle.
            SubId.ofString "fullscreen",
            AdaptiveSub.ofObservable
              (SubId.ofString "fullscreen")
              input.KeyboardDelta
              (fun _ d ->
                if d.Pressed |> Array.contains KeyCode.F11 then
                  match frameCtx.Context |> Window.tryGetService with
                  | ValueSome window -> window.ToggleFullscreen()
                  | ValueNone -> ())
          ]

      cached
