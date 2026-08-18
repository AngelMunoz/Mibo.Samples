module Defli3D.Raylib.Program

open System
open System.IO
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
// Defli3D — the windowed raylib frontend. The sim runs on the
// AdaptiveRaylibGame host: one Step per frame (input poll →
// subscriptions run → update → force), the renderers draw the
// forced frame. The MonoGame clients are Defli3D/MonoDX12 + thin
// clients (same Shared sim). Program assembly lives in Shared
// (Application.program + Input.subscriptions); this file only
// boots the host-specific bits and wires the renderers.
//
// Renderers (registered in draw order — the last renderer draws
// last, on top):
//   1. ONE Renderer3D — the world pass: ForwardPbrPipeline with a
//      shadow atlas (2048² — the whole 20×12 map fits one 4K atlas
//      with room to spare; GridSnapSize keeps the orbit shadows
//      stable).
//   2. ONE Renderer2D (noClear) — the HUD pass on top.
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

  // The assets are copied to the output directory; resolve them from
  // the exe location so the game runs from any working directory.
  let assetsBasePath = Path.Combine(AppContext.BaseDirectory, "assets")

  let boot(_ctx: AdaptiveFrameContext) = ()

  let program =
    Application.program
      boot
      cell
      (Input.subscriptions
        1.1
        (fun ctx ->
          InputMapper.subscribeStaticAdaptive
            Inputs.inputMap
            cell.Value.Actions
            ctx)
        cell
        shell)
    |> AdaptiveProgram.withProfiler profiler
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withAssetsBasePath assetsBasePath
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      let pipeline =
        ForwardPbrPipeline(
          shadowBiasConfig = {
            ShadowBiasConfig.defaults with
                DirectionalBias = 0.002f
                SlopeScaleBias = 0.0008f
          },
          shadowAtlasConfig = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
                GridSnapSize = 4f
          }
        )

      Renderer3D.create pipeline (fun ctx frame buffer ->
        world.Render(ctx, frame, buffer)))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (fun ctx frame buffer ->
        world.Hud(ctx, frame, buffer)))

  let game = new AdaptiveRaylibGame<Frame.RenderFrame>(program)

  game.Run()
  0
