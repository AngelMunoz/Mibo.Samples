module FPSSample.Raylib.HudView

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
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
  let crossColor = Mibo.RaylibColor.toRaylibColor HudLayout.crosshairColor
  let crossSize = HudLayout.crosshairSize

  buffer
  |> Draw.lineThick
    (0<RenderLayer>, crossColor, HudLayout.crosshairThickness)
    (Vector2(cx - crossSize, cy), Vector2(cx + crossSize, cy))
  |> ignore

  buffer
  |> Draw.lineThick
    (0<RenderLayer>, crossColor, HudLayout.crosshairThickness)
    (Vector2(cx, cy - crossSize), Vector2(cx, cy + crossSize))
  |> ignore

  // ── Health bar ────────────────────────────────────────────────────────────
  let barX = HudLayout.healthBarX
  let barY = HudLayout.healthBarY screenH
  let barW = HudLayout.healthBarW
  let barH = HudLayout.healthBarH

  buffer
  |> Draw.fillRect
    (0<RenderLayer>, Mibo.RaylibColor.toRaylibColor HudLayout.healthBarBackdrop)
    (Rectangle(barX, barY, barW, barH))
  |> ignore

  let healthPct = HudLayout.healthPercent model

  buffer
  |> Draw.fillRect
    (0<RenderLayer>,
     Mibo.RaylibColor.toRaylibColor(HudLayout.healthColor healthPct))
    (Rectangle(barX, barY, barW * healthPct, barH))
  |> ignore

  // ── Ammo counter ──────────────────────────────────────────────────────────
  buffer
  |> Draw.text {
    Font = font
    Text = HudLayout.ammoText model
    Position = Vector2(screenW - 180.0f, screenH - 35.0f)
    FontSize = HudLayout.ammoFontSize
    Spacing = 1.0f
    Color = Color.White
    Layer = 0<RenderLayer>
  }
  |> ignore

  // ── Score ─────────────────────────────────────────────────────────────────
  buffer
  |> Draw.text {
    Font = font
    Text = HudLayout.scoreText model
    Position = Vector2(20.0f, 20.0f)
    FontSize = HudLayout.scoreFontSize
    Spacing = 1.0f
    Color = Color.White
    Layer = 0<RenderLayer>
  }
  |> ignore

  // ── Hit feedback gray flash ───────────────────────────────────────────────
  if HudLayout.isHitFlash model then
    buffer
    |> Draw.fillRect
      (0<RenderLayer>,
       Mibo.RaylibColor.toRaylibColor(HudLayout.hitFlashColor model))
      (Rectangle(0.0f, 0.0f, screenW, screenH))
    |> ignore

  // ── Game over overlay ─────────────────────────────────────────────────────
  if HudLayout.isGameOver model then
    buffer
    |> Draw.fillRect
      (0<RenderLayer>,
       Mibo.RaylibColor.toRaylibColor HudLayout.gameOverOverlayColor)
      (Rectangle(0.0f, 0.0f, screenW, screenH))
    |> ignore

    buffer
    |> Draw.text {
      Font = font
      Text = HudLayout.gameOverText
      Position = Vector2(cx - 160.0f, cy + 40.0f)
      FontSize = HudLayout.gameOverFontSize
      Spacing = 1.0f
      Color = Color.White
      Layer = 0<RenderLayer>
    }
    |> ignore
