module FPSSample.MonoShared.HudView

open System
open System.Diagnostics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics
open FPSSample
open FPSSample.Types

// ── Wall-clock frame-rate counter ─────────────────────────────────────────────
// The host calls this view exactly once per Draw, so sampling a Stopwatch here
// measures the REAL render rate — unlike the fixed-timestep Tick dt, it exposes
// frame drops below the target FPS. This is render telemetry (not gameplay
// state), so it stays out of the routed GameModel as a module-private holder.
let private frameSw = Stopwatch.StartNew()
let private fpsState = ref(0.0f, 0.0f)

let private sampleFrameRate() =
  let elapsed = float32 frameSw.Elapsed.TotalSeconds
  frameSw.Restart()
  let ms = elapsed * 1000.0f

  let nextFps =
    if elapsed > 0.0f then
      let instant = 1.0f / elapsed
      let prev = fst !fpsState
      // EMA over ~the last second of frames; seeded from the first sample.
      if prev = 0.0f then
        instant
      else
        prev * 0.9f + instant * 0.1f
    else
      fst !fpsState

  fpsState := (nextFps, ms)

/// Renders the 2D HUD overlay: crosshair, health bar, ammo counter, score, FPS.
/// The font is loaded lazily from the content pipeline via the asset service.
let view
  (font: SpriteFont)
  (ctx: GameContext)
  (model: GameModel)
  (buffer: RenderBuffer2D)
  =
  sampleFrameRate()
  let smoothedFps, frameTimeMs = !fpsState

  let screenW = float32 ctx.WindowWidth
  let screenH = float32 ctx.WindowHeight

  // ── Crosshair ─────────────────────────────────────────────────────────────
  let cx = screenW * 0.5f
  let cy = screenH * 0.5f
  let crossColor = Mibo.MonoGameColor.toMonoGameColor HudLayout.crosshairColor
  let crossSize = HudLayout.crosshairSize

  buffer
    .line(
      Vector2(cx - crossSize, cy).ToNumerics(),
      Vector2(cx + crossSize, cy).ToNumerics(),
      crossColor |> Mibo.MonoGameColor.fromMonoGameColor
    )
    .line(
      Vector2(cx, cy - crossSize).ToNumerics(),
      Vector2(cx, cy + crossSize).ToNumerics(),
      crossColor |> Mibo.MonoGameColor.fromMonoGameColor
    )
    .drop()

  // ── Health bar ────────────────────────────────────────────────────────────
  let barX = HudLayout.healthBarX
  let barY = HudLayout.healthBarY screenH
  let barW = HudLayout.healthBarW
  let barH = HudLayout.healthBarH

  buffer
    .fillRect(
      HudLayout.healthBarX,
      HudLayout.healthBarY screenH,
      HudLayout.healthBarW,
      HudLayout.healthBarH,
      HudLayout.healthBarBackdrop
    )
    .drop()

  let healthPct = HudLayout.healthPercent model

  buffer
    .fillRect(
      barX,
      barY,
      barW * healthPct,
      barH,
      HudLayout.healthColor healthPct
    )
    .text(
      {
        // ── Ammo counter ──────────────────────────────────────────────────────────
        Font = font
        Text = HudLayout.ammoText model
        Position = Vector2(screenW - 180.0f, screenH - 35.0f)
        Scale = 1.0f
        Color = Color.White
        Layer = 0<RenderLayer>
      }
    )
    .text(
      {
        // ── Score ─────────────────────────────────────────────────────────────────
        Font = font
        Text = HudLayout.scoreText model
        Position = Vector2(20.0f, 20.0f)
        Scale = 1.2f
        Color = Color.White
        Layer = 0<RenderLayer>
      }
    )
    .text(
      {
        // ── FPS (wall-clock render rate) ──────────────────────────────────────────
        Font = font
        Text = $"FPS: {int smoothedFps}  ({frameTimeMs:F1}ms)"
        Position = Vector2(20.0f, 48.0f)
        Scale = 1.0f
        Color = Color.White
        Layer = 0<RenderLayer>
      }
    )
    .drop()

  // ── Game over overlay ─────────────────────────────────────────────────────
  if HudLayout.isGameOver model then
    buffer
      .fillRect(0f, 0f, screenW, screenH, HudLayout.gameOverOverlayColor)
      .text(
        {
          Font = font
          Text = HudLayout.gameOverText
          Position = Vector2(cx - 160.0f, cy + 40.0f)
          Scale = 1.4f
          Color = Color.White
          Layer = 0<RenderLayer>
        }
      )
      .drop()
