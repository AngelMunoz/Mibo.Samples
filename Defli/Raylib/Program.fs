module Defli.Raylib.Program

open System
open System.IO
open Mibo
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli
open Defli.State

// ─────────────────────────────────────────────────────────────
// Defli — the windowed raylib frontend. The sim runs on the
// AdaptiveRaylibGame host: one Step per frame (input poll →
// subscriptions run → update → force), the renderers draw the
// forced frame. The MonoGame clients are Defli/MonoDX12 + thin
// clients (same Shared sim). Program assembly lives in Shared
// (Application.program + Input.subscriptions); this file only
// boots the host-specific bits and wires the renderers.
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

  // The assets are copied to the output directory; resolve them from
  // the exe location so the game runs from any working directory.
  let assetsBasePath = Path.Combine(AppContext.BaseDirectory, "assets")

  // The raylib loader forces TRILINEAR filtering on every texture
  // (docs/assets.md): a gutterless spritesheet sampled bilinearly
  // at tile borders bleeds adjacent (black) texels in — the seam
  // lines between tiles. Point filtering stops it. Applied ONCE at
  // boot (mutates the cached texture's sampler — not per frame).
  let boot(ctx: AdaptiveFrameContext) =
    let assets = ctx.Context |> GameContext.getService<IAssets>

    assets.Texture Tiles.SheetPath
    |> Texture.filter TextureFilter.Point
    |> ignore

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
      Renderer2D.create(WorldView.worldView shell vfx aura))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (WorldView.hudView shell))

  let game = new AdaptiveRaylibGame<Frame.RenderFrame>(program)

  game.Run()
  0
