namespace Defli3D.MonoGame

open System.Numerics
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Defli3D
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// DiagnosticsView — the F3 overlay lines (Kimo's
// DiagnosticsViewExtensions pattern), ported from Defli/MonoDX12.
// The FrameDiag object is shell-owned; the WorldDiag rides the frame.
// ─────────────────────────────────────────────────────────────

[<AutoOpen>]
module DiagnosticsViewExtensions =

  type RenderBuffer2D with

    /// Draws the frame-diagnostics line (the caller gates visibility via
    /// FrameDiag.Visible). Anchored at the given position.
    member inline buffer.frameDiagnostics
      (font: SpriteFont, diag: FrameDiag, at: Vector2)
      : RenderBuffer2D =
      let yellow = Mibo.Color.rgb 255uy 210uy 0uy

      buffer.text(font, diag.Display, at, 1f, tint = yellow, layer = Layers.Hud)

    /// Draws the world-diagnostics line (gated like frameDiagnostics).
    member inline buffer.worldDiagnostics
      (font: SpriteFont, diag: WorldDiag, at: Vector2)
      : RenderBuffer2D =
      let yellow = Mibo.Color.rgb 255uy 210uy 0uy

      buffer.text(font, diag.Display, at, 1f, tint = yellow, layer = Layers.Hud)
