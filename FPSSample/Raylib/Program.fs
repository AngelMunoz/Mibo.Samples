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
// Note: qualified names — the framework now registers its own
// Mibo.Elmish.AudioService, which would shadow the sample's adapter type.
let animService = View.EnemyAnimationService()
let audioService = FPSSample.Raylib.AudioService()

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

// The sound bank: keys → loose files, loaded before init runs. Events and
// audio messages carry only the keys (Assets.Keys); raylib resolves them from
// disk, relative to the program's asset base path.
let private bank: RaylibProgram.BankEntry list =
  let inline sound key path =
    RaylibProgram.BankEntry.Sound(key, path)

  [
    sound Assets.Keys.fire Assets.gunSoundSingle
    sound Assets.Keys.reloadFast Assets.reloadFast
    sound Assets.Keys.reloadRifle Assets.reloadRifle
    sound Assets.Keys.reloadHeavy Assets.reloadHeavy
    sound Assets.Keys.bite Assets.bite
    sound Assets.Keys.childLaugh Assets.childLaugh
    sound Assets.Keys.gasp Assets.gasp
    sound Assets.Keys.injured Assets.injured
    sound Assets.Keys.footstepWalk Assets.footstepsWalking
    sound Assets.Keys.footstepRun Assets.footstepsRunning
    sound Assets.Keys.robotic[0] Assets.roboticSounds[0]
    sound Assets.Keys.robotic[1] Assets.roboticSounds[1]
    sound Assets.Keys.robotic[2] Assets.roboticSounds[2]
    sound Assets.Keys.robotic[3] Assets.roboticSounds[3]

    RaylibProgram.BankEntry.Music("battle", Assets.spaceMusicBattle)
  ]

[<EntryPoint>]
let main _ =
  Raylib.SetTraceLogLevel(TraceLogLevel.Warning)

  let program =
    Program.mkProgram init update
    |> Program.withAssetsBasePath(AppContext.BaseDirectory)
    |> RaylibProgram.withBank bank
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo FPS Sample (raylib)"
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
