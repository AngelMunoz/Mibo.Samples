module Defli.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Defli
open Defli.State

// ─────────────────────────────────────────────────────────────
// Defli — the windowed MonoGame frontend. The sim runs on the
// AdaptiveMonoGameGame host: one Step per frame (input poll →
// subscriptions run → update → force), the renderers draw the
// forced frame. The raylib client is Defli/Raylib (same Shared
// sim). Program assembly lives in Shared (Application.program +
// Input.subscriptions); this file only wires the renderers.
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
    |> GameConfig.withHeight 800
    |> GameConfig.withTitle "Defli"
    |> GameConfig.withTargetFPS 60

  let vfx = VfxView()

  let program =
    // Raw XNA wheel is ±120 per notch: the per-notch zoom base keeps
    // one notch = ×1.1, same as the raylib client.
    Application.program
      ignore
      (fun () -> cell.Value)
      (Input.subscriptions (1.1 ** (1.0 / 120.0)) cell shell)
    |> AdaptiveProgram.withObserver(fun () ->
      AdaptiveProgram.observe(fun _ -> Diagnostics.update shell.Diag))
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.create(WorldView.worldView shell vfx))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (WorldView.hudView shell))
    |> AdaptiveMonoGameProgram.ofProgram
    |> AdaptiveMonoGameProgram.withConfig(fun (game, _) ->
      game.Content.RootDirectory <- "Content")

  let game = new AdaptiveMonoGameGame<Frame.RenderFrame>(program)

  game.Run()
  0
