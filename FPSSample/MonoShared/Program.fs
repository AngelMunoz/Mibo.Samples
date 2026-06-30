namespace FPSSample.MonoShared

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Input
open FPSSample
open FPSSample.Types
open FPSSample.MonoShared.View
open FPSSample.MonoShared.HudView
open Mibo.Input

/// MonoGame-specific program wiring using the shared Env pattern.
/// Thin client projects (DesktopGL, WindowsDX) call this and pass the result
/// to MiboGame.
module Program =

  // ── Composition Root ────────────────────────────────────────────────────────
  let animService = EnemyAnimationService()

  let env: Env = { Animation = animService }

  let private init =
    GameLoop.createInit env (fun ctx ->
      // Capture the mouse for FPS-style look (re-center after poll)
      Input.getService(ctx).SetMouseCapture(MouseCapture.Captured))

  let private update = GameLoop.createUpdate env

  let private subscribe =
    GameLoop.createSubscribe(fun ctx ->
      InputMapper.subscribeStatic
        Game.inputMap
        (fun a -> Msg.InputMapped a)
        ctx)

  /// Creates the full Mibo Program with MonoGame-specific animation wiring
  /// and renderers. Pass the result to MiboGame.
  let create() : Program<GameModel, Msg> =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo FPS Sample (MonoGame)"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Msg.Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = {
            ShadowAtlasConfig.defaults with
                Resolution = 4096
                GridSnapSize = 16.0f
          }
        )

      Renderer3D.create pipeline (View.view animService))
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (fun ctx model buffer ->
        let assets = GameContext.getService<IAssets> ctx

        let font =
          assets.GetOrCreate(
            "diagnosticsFont",
            fun () -> assets.Font "diagnostics"
          )

        view font ctx model buffer))
