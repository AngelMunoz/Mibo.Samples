module Defli3D.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Mibo.Adaptive
open Mibo.Diagnostics
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
  let shell = { MiddleDown = false; ShowDiag = false }

  // The frame profiler: handed to the program below, measured by the
  // host. The overlay starts hidden, so measurement starts off too;
  // F3 turns both on.
  let profiler =
    FrameProfiler(FrameProfiler.DefaultWindow, canScreenshot = true)

  profiler.Enabled <- false

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

  // The sound bank: Application.AudioKeys → pipeline assets (built in
  // Content/Content.mgcb). AdaptiveMonoGameProgram.withBank loads it before
  // init runs; a missing pipeline asset throws there, where the mistake
  // belongs.
  let bank: AdaptiveMonoGameProgram.BankEntry list = [
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

    // The single music channel: calm building phase <-> battle during waves
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
    |> AdaptiveProgram.withProfiler profiler
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
    |> AdaptiveMonoGameProgram.withBank bank
    |> AdaptiveMonoGameProgram.withConfig(fun (game, _) ->
      game.Content.RootDirectory <- "Content")

  let game = new AdaptiveMonoGameGame<Frame.RenderFrame>(program)

  game.Run()
  0
