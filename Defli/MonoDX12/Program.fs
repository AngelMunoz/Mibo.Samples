module Defli.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Defli.World

// ─────────────────────────────────────────────────────────────
// Defli — the windowed MonoGame frontend. The sim runs on the
// AdaptiveGame host: one Step per frame (input poll → shell phase →
// Router.step → force), the renderers draw the forced frame. The
// raylib client is Defli/Raylib (same Shared sim).
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let cell = WorldCell(World.init WorldConfig.defaults)
  let shell = Shell()

  let config =
    GameConfig.defaultConfig
    |> GameConfig.withWidth 1280
    |> GameConfig.withHeight 800
    |> GameConfig.withTitle "Defli"
    |> GameConfig.withTargetFPS 60

  let vfx = VfxView()

  let program =
    Application.windowedProgram cell shell
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.create(WorldView.worldView shell vfx))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (WorldView.hudView shell))
    |> AdaptiveMonoGameProgram.ofProgram
    |> AdaptiveMonoGameProgram.withConfig(fun (game, _) ->
      game.Content.RootDirectory <- "Content")

  let game = new AdaptiveGame<Frame.RenderFrame>(program)

  game.Run()
  0
