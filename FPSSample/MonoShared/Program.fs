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
  // Qualified names — the framework now registers its own
  // Mibo.Elmish.AudioService, which would shadow the sample's adapter type.
  let animService = EnemyAnimationService()
  let audioService = FPSSample.MonoShared.AudioService()

  let env: Env = {
    Animation = animService
    Audio = audioService
  }

  let private init =
    GameLoop.createInit env (fun ctx ->
      // Capture the mouse for FPS-style look (re-center after poll)
      Input.getService(ctx).SetMouseCapture(MouseCapture.Captured)
      env.Audio.Init ctx)

  let private update = GameLoop.createUpdate env

  let private subscribe =
    GameLoop.createSubscribe(fun ctx ->
      InputMapper.subscribeStatic
        Game.inputMap
        (fun a -> Msg.InputMapped a)
        ctx)

  /// The sound bank: keys → pipeline assets, loaded before init runs. Events
  /// and audio messages carry only the keys (Assets.Keys); the MGCB builds
  /// each sound under the name listed here (MonoShared/Content/Content.mgcb).
  let private bank: MonoGameProgram.BankEntry list =
    let inline sound key asset =
      MonoGameProgram.BankEntry.Sound(key, Pipeline asset)

    [
      sound Assets.Keys.fire "gun_sounds/7.62x39/762x39 Single MP3"
      sound Assets.Keys.reloadFast "gun_sounds/reloads/reload-fast"
      sound Assets.Keys.reloadRifle "gun_sounds/reloads/reload-rifle"
      sound Assets.Keys.reloadHeavy "gun_sounds/reloads/reload-heavy"
      sound Assets.Keys.bite "horror_sfx/Bite"
      sound Assets.Keys.childLaugh "horror_sfx/Child laugh"
      sound Assets.Keys.gasp "horror_sfx/Gasp_3"
      sound Assets.Keys.injured "horror_sfx/Injured"
      sound Assets.Keys.footstepWalk "horror_sfx/Footsteps_walking"
      sound Assets.Keys.footstepRun "horror_sfx/Footsteps_ running"
      sound Assets.Keys.robotic[0] "horror_sfx/Robotic_bass"
      sound Assets.Keys.robotic[1] "horror_sfx/robotic_groan_3"
      sound Assets.Keys.robotic[2] "horror_sfx/robotic_hiss"
      sound Assets.Keys.robotic[3] "horror_sfx/Scream_Robotic"

      MonoGameProgram.BankEntry.Music(
        "battle",
        Pipeline "space_music_pack/battle"
      )
    ]

  /// Creates the full Mibo program (as a MonoGameProgram — the sound bank is
  /// a MonoGame-side builder) with MonoGame-specific animation wiring and
  /// renderers. Pass the result to MiboGame.
  let create() : MonoGameProgram<GameModel, Msg> =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo FPS Sample (MonoGame)"
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
    |> MonoGameProgram.ofProgram
    |> MonoGameProgram.withBank bank
