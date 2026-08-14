namespace PrimitiveGallery

open Mibo.Adaptive
open Mibo.Elmish

module Application =

  /// Builds the graph: the frame force reads the CURRENT state's screen
  /// projection at the end of every Step.
  let inline init
    (getState: unit -> State)
    (_ctx: AdaptiveFrameContext)
    : AdaptiveInit<Frame.RenderFrame> =
    AdaptiveInit.ofFrameBuilder(Frame.force getState)

  /// No per-frame simulation except the elapsed-time accumulator the 3D
  /// rotation reads — Update just advances the clock every Step.
  let update
    (getState: unit -> State)
    (_ctx: AdaptiveContext)
    (gameTime: GameTime)
    : unit =
    let state = getState()
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    Transaction.run(fun () ->
      CVal.set (AVal.getValue state.Elapsed + dt) state.Elapsed)
    |> ignore

  /// The adaptive program: init builds the frame force and the subscription
  /// projection (boot runs host wiring first); update is the no-op sim.
  let inline program
    (boot: AdaptiveFrameContext -> unit)
    (getState: unit -> State)
    (subscribe: AdaptiveFrameContext -> amap<SubId, AdaptiveSub>)
    : AdaptiveProgram<Frame.RenderFrame> =
    AdaptiveProgram.mkProgram
      (fun ctx ->
        boot ctx
        (init getState ctx) |> AdaptiveInit.withSubscriptions subscribe)
      (update getState)
