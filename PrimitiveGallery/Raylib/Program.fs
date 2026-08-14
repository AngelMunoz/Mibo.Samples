module PrimitiveGallery.Raylib.Program

open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open PrimitiveGallery

// ─────────────────────────────────────────────────────────────
// PrimitiveGallery — the windowed raylib frontend. The adaptive
// shell lives in Shared (Application.program + Input.subscriptions);
// this file only boots the host and wires the two renderers:
//   1. ONE Renderer3D — the 3D shapes / line3D pass (ForwardPbrPipeline).
//   2. ONE Renderer2D (noClear) — the HUD pass on top.
// The renderer factories receive no context: the view functions are
// passed directly, already in (ctx)(frame)(buffer) curried form.
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let cell = StateCell(State.init())

  let config =
    GameConfig.defaultConfig
    |> GameConfig.withWidth 1280
    |> GameConfig.withHeight 720
    |> GameConfig.withTitle "PrimitiveGallery"
    |> GameConfig.withTargetFPS 60

  let boot(_ctx: AdaptiveFrameContext) = ()

  // The single 2D pass draws the shape cells (draw2D) and then the HUD;
  // draw2D is a no-op for Shapes3D, and the HUD dispatch picks hud2D
  // (title + help only, for Shapes2D + Split) or hud3D (3D labels +
  // title + help, for Shapes3D).
  let hud
    (ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    =
    Screen2DView.draw2D ctx frame buffer

    match frame.Screen with
    | Screen.Shapes3D -> Screen3DView.hud3D ctx frame buffer
    | _ -> Screen2DView.hud2D ctx frame buffer

  let program =
    Application.program boot (fun () -> cell.Value) (Input.subscriptions cell)
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      let pipeline =
        ForwardPbrPipeline(
          shadowBiasConfig = ShadowBiasConfig.defaults,
          shadowAtlasConfig = ShadowAtlasConfig.defaults
        )

      Renderer3D.create pipeline Screen3DView.draw3D)
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear hud)

  let game = new AdaptiveRaylibGame<Frame.RenderFrame>(program)
  game.Run()
  0
