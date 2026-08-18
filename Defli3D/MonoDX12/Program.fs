module Defli3D.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Defli3D
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Defli3D — the windowed MonoGame frontend. The sim runs on the
// AdaptiveMonoGameGame host: one Step per frame (input poll →
// subscriptions run → update → force), the renderers draw the
// forced frame. One Renderer3D (ForwardPipeline + shadow atlas)
// renders the world, one noClear Renderer2D draws the HUD. The
// raylib client is Defli3D/Raylib (same Shared sim); the MonoGL/
// MonoVK/MonoDX11 clients link these exact sources.
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let shell = {
    MiddleDown = false
    Diag = FrameDiag()
  }

  let cell = StateCell(State.init WorldConfig.defaults)

  let config =
    GameConfig.defaultConfig
    |> GameConfig.withWidth 1280
    |> GameConfig.withHeight 720
    |> GameConfig.withResizable
    |> GameConfig.withMinWidth 960
    |> GameConfig.withMinHeight 540
    |> GameConfig.withTitle "Defli3D"
    |> GameConfig.withTargetFPS 60

  let vfx = VfxView()
  let world = WorldView(shell, vfx)

  let program =
    // Raw XNA wheel is ±120 per notch: the per-notch zoom base keeps
    // one notch = ×1.1, same as the raylib client.
    Application.program
      ignore
      cell
      (Input.subscriptions
        (1.1 ** (1.0 / 120.0))
        (fun ctx ->
          InputMapper.subscribeStaticAdaptive
            Inputs.inputMap
            cell.Value.Actions
            ctx)
        cell
        shell)
    |> AdaptiveProgram.withObserver(fun () ->
      AdaptiveProgram.observe(fun _ -> Diagnostics.update shell.Diag))
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
          }
        )

      Renderer3D.create pipeline (fun ctx frame buffer ->
        world.Render(ctx, frame, buffer)))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (fun ctx frame buffer ->
        world.Hud(ctx, frame, buffer)))
    |> AdaptiveMonoGameProgram.ofProgram
    |> AdaptiveMonoGameProgram.withConfig(fun (game, _) ->
      game.Content.RootDirectory <- "Content")

  let game = new AdaptiveMonoGameGame<Frame.RenderFrame>(program)

  game.Run()
  0
