module Defli3D.Raylib.Program

open System
open System.IO
open Mibo.Adaptive
open Mibo.Elmish
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
  let shell = {
    MiddleDown = false
    Diag = FrameDiag()
  }

  let cell = StateCell(State.init WorldConfig.defaults)

  let config =
    GameConfig.defaultConfig
    |> GameConfig.withWidth 1280
    |> GameConfig.withHeight 800
    |> GameConfig.withTitle "Defli3D"
    |> GameConfig.withTargetFPS 60

  let vfx = VfxView()

  // The assets are copied to the output directory; resolve them from
  // the exe location so the game runs from any working directory.
  let assetsBasePath = Path.Combine(AppContext.BaseDirectory, "assets")

  let boot(_ctx: AdaptiveFrameContext) = ()

  let program =
    Application.program
      boot
      (fun () -> cell.Value)
      (Input.subscriptions 1.1 cell shell)
    |> AdaptiveProgram.withObserver(fun () ->
      AdaptiveProgram.observe(fun _ -> Diagnostics.update shell.Diag))
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

      Renderer3D.create pipeline (WorldView.worldView shell vfx))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (WorldView.hudView shell))

  let game = new AdaptiveRaylibGame<Frame.RenderFrame>(program)

  game.Run()
  0
