module FPSSample.Raylib.Program

open System
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Input
open Raylib_cs
open FPSSample
open FPSSample.Types
open Mibo.Input

// ── Composition Root ──────────────────────────────────────────────────────────
// Create the env with backend-specific services, then wire init/update/subscribe.
let animService = View.EnemyAnimationService()
let audioService = AudioService()

let env: Env = {
  Animation = animService
  Audio = audioService
}

let init =
  GameLoop.createInit env (fun ctx ->
    // Capture the mouse for FPS-style look (native raylib DisableCursor)
    Input.getService(ctx).SetMouseCapture(MouseCapture.Captured)
    (audioService :> IAudioService).Init(ctx))

let update = GameLoop.createUpdate env

let subscribe =
  GameLoop.createSubscribe(fun ctx ->
    InputMapper.subscribeStatic Game.inputMap (fun a -> Msg.InputMapped a) ctx)

[<EntryPoint>]
let main _ =
  Raylib.SetTraceLogLevel(TraceLogLevel.Warning)

  let program =
    Program.mkProgram init update
    |> Program.withAssetsBasePath(AppContext.BaseDirectory)
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo FPS Sample (raylib)"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Msg.Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPbrPipeline(
          shadowBiasConfig = {
            DirectionalBias = 0.002f
            PointBias = 0.01f
            SpotBias = 0.001f
            SlopeScaleBias = 0.001f
          },
          shadowAtlasConfig = {
            ShadowAtlasConfig.defaults with
                Resolution = 4096
          }
        )

      Renderer3D.create pipeline (View.view animService))
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear HudView.view)

  let game = new RaylibGame<GameModel, Msg>(program)
  game.Run()
  0
