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

  // ── Hit feedback overlay ──────────────────────────────────────────────────
  // On hit the screen snaps to near-opaque black, then fades down to a dark-gray
  // tint and holds there until the HitEffectTimer expires, at which point the
  // overlay is removed entirely. While active the opacity never drops below the
  // floor, so the player always sees at least the dark-gray tint.

  /// Peak opacity (0-1) right after taking damage ("sees almost nothing").
  let hitFlashPeakAlpha = 0.80f

  /// Floor opacity (0-1) the fade settles on and holds at until removal.
  let hitFlashFloorAlpha = 0.30f

  /// Dark-gray tint the fade settles on (RGB).
  let hitFlashDarkGray: struct (byte * byte * byte) = struct (40uy, 40uy, 45uy)

  /// Remaining-timer fraction (0-1) at which the fade finishes and the overlay
  /// starts holding at the floor until the timer removes it. 0.5 = fade over the
  /// first half of the duration, hold over the second half.
  let hitFlashHoldAtPct = 0.5f

  /// Hit-feedback overlay color. Drives the snap → fade → hold → remove sequence
  /// from the remaining HitEffectTimer.
  let inline hitFlashColor(model: GameModel) : Mibo.Color =
    let pct =
      Math.Clamp(model.HitEffectTimer / Constants.HitEffectDuration, 0.0f, 1.0f)

    // fadeProgress: 1 at the peak, 0 once the floor is reached (then held).
    let fadeProgress =
      Math.Clamp(
        (pct - hitFlashHoldAtPct) / (1.0f - hitFlashHoldAtPct),
        0.0f,
        1.0f
      )

    let alphaPct =
      hitFlashFloorAlpha
      + (hitFlashPeakAlpha - hitFlashFloorAlpha) * fadeProgress

    let alpha = alphaPct * 255.0f
    let inv = 1.0f - fadeProgress
    let struct (gr, gg, gb) = hitFlashDarkGray

    Mibo.Color.create
      (byte(float32 gr * inv))
      (byte(float32 gg * inv))
      (byte(float32 gb * inv))
      (byte alpha)

  /// Whether the hit-feedback overlay should render.
  let inline isHitFlash(model: GameModel) : bool = model.HitEffectTimer > 0.0f

  /// Whether the game over overlay should show.
  let inline isGameOver(model: GameModel) : bool = model.PlayerHealth <= 0.0f
