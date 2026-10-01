module FPSSample.Raylib.HudView

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open FPSSample
open FPSSample.Types

/// Renders the 2D HUD overlay: crosshair, health bar, ammo counter, score.
let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  let screenW = float32 ctx.WindowWidth
  let screenH = float32 ctx.WindowHeight
  let font = Raylib.GetFontDefault()

  // ── Crosshair ─────────────────────────────────────────────────────────────
  let cx = screenW * 0.5f
  let cy = screenH * 0.5f
  let crossColor = HudLayout.crosshairColor
  let crossSize = HudLayout.crosshairSize

  buffer
    .lineThick(
      Vector2(cx - crossSize, cy),
      Vector2(cx + crossSize, cy),
      crossColor,
      thickness = HudLayout.crosshairThickness
    )
    .drop()

  buffer
    .lineThick(
      Vector2(cx, cy - crossSize),
      Vector2(cx, cy + crossSize),
      crossColor,
      thickness = HudLayout.crosshairThickness
    )
    .drop()

  // ── Health bar ────────────────────────────────────────────────────────────
  let barX = HudLayout.healthBarX
  let barY = HudLayout.healthBarY screenH
  let barW = HudLayout.healthBarW
  let barH = HudLayout.healthBarH

  buffer.fillRect(barX, barY, barW, barH, HudLayout.healthBarBackdrop).drop()

  let healthPct = HudLayout.healthPercent model

  buffer
    .fillRect(
      barX,
      barY,
      barW * healthPct,
      barH,
      HudLayout.healthColor healthPct
    )
    .drop()

  // ── Ammo counter ──────────────────────────────────────────────────────────
  buffer
    .text(
      {
        Font = font
        Text = HudLayout.ammoText model
        Position = Vector2(screenW - 180.0f, screenH - 35.0f)
        FontSize = HudLayout.ammoFontSize
        Spacing = 1.0f
        Color = Color.White
        Layer = 0<RenderLayer>
      }
      : Command2D.TextState
    )
    .drop()

  // ── Score ─────────────────────────────────────────────────────────────────
  buffer
    .text(
      {
        Font = font
        Text = HudLayout.scoreText model
        Position = Vector2(20.0f, 20.0f)
        FontSize = HudLayout.scoreFontSize
        Spacing = 1.0f
        Color = Color.White
        Layer = 0<RenderLayer>
      }
      : Command2D.TextState
    )
    .drop()

  // ── Game over overlay ─────────────────────────────────────────────────────
  if HudLayout.isGameOver model then
    buffer
      .fillRect(0.0f, 0.0f, screenW, screenH, HudLayout.gameOverOverlayColor)
      .drop()

    buffer
      .text(
        {
          Font = font
          Text = HudLayout.gameOverText
          Position = Vector2(cx - 160.0f, cy + 40.0f)
          FontSize = HudLayout.gameOverFontSize
          Spacing = 1.0f
          Color = Color.White
          Layer = 0<RenderLayer>
        }
        : Command2D.TextState
      )
      .drop()
