module PrimitiveGallery.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open PrimitiveGallery

// ─────────────────────────────────────────────────────────────
// PrimitiveGallery — the windowed MonoGame frontend. The gallery
// is static, so the AdaptiveMonoGameGame host just forces the frame
// each Step; the renderers draw the forced frame. One Renderer3D
// (ForwardPipeline) renders the 3D shapes, one noClear Renderer2D
// draws the 2D cells and the HUD. The MonoGL/MonoVK/MonoDX11
// clients link these exact sources.
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
    | _ -> Screen3DView.hud2D ctx frame buffer

  let program =
    Application.program ignore (fun () -> cell.Value) (Input.subscriptions cell)
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = ShadowAtlasConfig.defaults
        )

      Renderer3D.create pipeline Screen3DView.draw3D)
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear hud)
    |> AdaptiveMonoGameProgram.ofProgram
    |> AdaptiveMonoGameProgram.withConfig(fun (game, _) ->
      game.Content.RootDirectory <- "Content")

  let game = new AdaptiveMonoGameGame<Frame.RenderFrame>(program)

  game.Run()
  0
