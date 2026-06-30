namespace FPSSample

open System
open FPSSample.Types

/// <summary>
/// Shared HUD layout constants and text computation, used by all backend HUD views.
/// The backends differ in their Draw2D primitives (lineThick vs line, FontSize vs Scale,
/// Rectangle constructors), but the layout math, colors, and strings are identical.
/// </summary>
module HudLayout =

  // ── Layout constants ──────────────────────────────────────────────────────

  let crosshairSize = 8.0f
  let crosshairThickness = 2.0f

  let healthBarX = 20.0f
  let healthBarH = 20.0f
  let healthBarW = 200.0f

  let ammoFontSize = 20.0f
  let scoreFontSize = 24.0f
  let gameOverFontSize = 28.0f

  // ── Computed values ───────────────────────────────────────────────────────

  /// Health bar Y position relative to screen height.
  let inline healthBarY(screenH: float32) : float32 = screenH - 40.0f

  /// Health percentage [0..1].
  let inline healthPercent(model: GameModel) : float32 =
    Math.Max(0.0f, model.PlayerHealth / Constants.PlayerMaxHealth)

  /// Health bar color based on percentage (green/yellow/red).
  let inline healthColor(pct: float32) : Mibo.Color =
    if pct > 0.5f then Mibo.Color.rgb 80uy 220uy 80uy
    elif pct > 0.25f then Mibo.Color.rgb 220uy 220uy 80uy
    else Mibo.Color.rgb 220uy 80uy 80uy

  /// Crosshair color.
  let crosshairColor: Mibo.Color = Mibo.Color.create 255uy 255uy 255uy 180uy

  /// Backdrop color for health bar.
  let healthBarBackdrop: Mibo.Color = Mibo.Color.create 0uy 0uy 0uy 150uy

  /// Ammo text string.
  let inline ammoText(model: GameModel) : string =
    if model.IsReloading then
      "Reloading..."
    else
      $"Ammo: {model.Ammo}/{Constants.MaxAmmo}"

  /// Score text string.
  let inline scoreText(model: GameModel) : string = $"Score: {model.Score}"

  /// Game over overlay text.
  let gameOverText: string = "GAME OVER - Press R to Restart"

  /// Game over overlay color.
  let gameOverOverlayColor: Mibo.Color = Mibo.Color.create 200uy 0uy 0uy 80uy

  /// Tracer line color (constant RGBA).
  let tracerLineColor: Mibo.Color = Mibo.Color.rgb 255uy 230uy 100uy

  /// Whether the game over overlay should show.
  let inline isGameOver(model: GameModel) : bool = model.PlayerHealth <= 0.0f
