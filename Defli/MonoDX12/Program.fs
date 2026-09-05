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
  let aura = AuraView()

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
      Renderer2D.create(WorldView.worldView shell vfx aura))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (WorldView.hudView shell))
    |> AdaptiveMonoGameProgram.ofProgram
    // The sound bank: Application.AudioKeys → pipeline assets (built in
    // Content/Content.mgcb). AdaptiveMonoGameProgram.withBank loads it before
    // init runs; a missing pipeline asset throws there, where the mistake
    // belongs.
    |> AdaptiveMonoGameProgram.withBank [
      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.shoot,
        Pipeline "gun_sounds/7.62x39/762x39 Single MP3"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.cannonFire,
        Pipeline "explosions/Medium_Explosion_2"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.impact,
        Pipeline "explosions/Small_Explosion"
      )

      // The death explosion — the loudest hit in the mix.
      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.enemyDown,
        Pipeline "explosions/Medium_Explosion_1"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.baseHit,
        Pipeline "horror_sfx/Scream_Robotic"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.waveStart,
        Pipeline "space_music_pack/fx/start-level"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.waveClear,
        Pipeline "horror_sfx/Child laugh"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.place,
        Pipeline "sfx_jump"
      )

      AdaptiveMonoGameProgram.BankEntry.Sound(
        Application.AudioKeys.upgrade,
        Pipeline "sfx_jump"
      )

      // The single music channel: calm building phase ↔ battle during waves
      // (switched by the wave lifecycle; see Application).
      AdaptiveMonoGameProgram.BankEntry.Music(
        Application.AudioKeys.musicCalm,
        Pipeline "space_music_pack/menu"
      )

      AdaptiveMonoGameProgram.BankEntry.Music(
        Application.AudioKeys.musicBattle,
        Pipeline "space_music_pack/battle"
      )
    ]
    |> AdaptiveMonoGameProgram.withConfig(fun (game, _) ->
      game.Content.RootDirectory <- "Content")

  let game = new AdaptiveMonoGameGame<Frame.RenderFrame>(program)

  game.Run()
  0
