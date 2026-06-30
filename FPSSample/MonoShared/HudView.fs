module FPSSample.MonoShared.HudView

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open FPSSample
open FPSSample.Types

/// Renders the 2D HUD overlay: crosshair, health bar, ammo counter, score.
/// The font is loaded lazily from the content pipeline via the asset service.
let view
  (font: SpriteFont)
  (ctx: GameContext)
  (model: GameModel)
  (buffer: RenderBuffer2D)
  =
  let screenW = float32 ctx.WindowWidth
  let screenH = float32 ctx.WindowHeight

  // ── Crosshair ─────────────────────────────────────────────────────────────
  let cx = screenW * 0.5f
  let cy = screenH * 0.5f
  let crossColor = Mibo.MonoGameColor.toMonoGameColor HudLayout.crosshairColor
  let crossSize = HudLayout.crosshairSize

  buffer
  |> Draw.line
    (0<RenderLayer>, crossColor)
    (Vector2(cx - crossSize, cy), Vector2(cx + crossSize, cy))
  |> ignore

  buffer
  |> Draw.line
    (0<RenderLayer>, crossColor)
    (Vector2(cx, cy - crossSize), Vector2(cx, cy + crossSize))
  |> ignore

  // ── Health bar ────────────────────────────────────────────────────────────
  let barX = int HudLayout.healthBarX
  let barY = int(HudLayout.healthBarY screenH)
  let barW = int HudLayout.healthBarW
  let barH = int HudLayout.healthBarH

  buffer
  |> Draw.fillRect
    (0<RenderLayer>,
     Mibo.MonoGameColor.toMonoGameColor HudLayout.healthBarBackdrop)
    (Rectangle(barX, barY, barW, barH))
  |> ignore

  let healthPct = HudLayout.healthPercent model

  buffer
  |> Draw.fillRect
    (0<RenderLayer>,
     Mibo.MonoGameColor.toMonoGameColor(HudLayout.healthColor healthPct))
    (Rectangle(barX, barY, int(float32 barW * healthPct), barH))
  |> ignore

  // ── Ammo counter ──────────────────────────────────────────────────────────
  buffer
  |> Draw.text {
    Font = font
    Text = HudLayout.ammoText model
    Position = Vector2(screenW - 180.0f, screenH - 35.0f)
    Scale = 1.0f
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
    Scale = 1.2f
    Color = Color.White
    Layer = 0<RenderLayer>
  }
  |> ignore

  // ── Hit feedback gray flash ───────────────────────────────────────────────
  if HudLayout.isHitFlash model then
    buffer
    |> Draw.fillRect
      (0<RenderLayer>,
       Mibo.MonoGameColor.toMonoGameColor(HudLayout.hitFlashColor model))
      (Rectangle(0, 0, int screenW, int screenH))
    |> ignore

  // ── Game over overlay ─────────────────────────────────────────────────────
  if HudLayout.isGameOver model then
    buffer
    |> Draw.fillRect
      (0<RenderLayer>,
       Mibo.MonoGameColor.toMonoGameColor HudLayout.gameOverOverlayColor)
      (Rectangle(0, 0, int screenW, int screenH))
    |> ignore

    buffer
    |> Draw.text {
      Font = font
      Text = HudLayout.gameOverText
      Position = Vector2(cx - 160.0f, cy + 40.0f)
      Scale = 1.4f
      Color = Color.White
      Layer = 0<RenderLayer>
    }
    |> ignore
