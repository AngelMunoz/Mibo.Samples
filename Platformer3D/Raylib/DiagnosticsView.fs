module Platformer3D.Raylib.DiagnosticsView

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Platformer3D.Raylib.Types

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  // Sample the wall-clock interval since the previous Draw — the real render rate.
  model.Diag.Tick()
  let pos = model.Physics.Position

  buffer
  |> Draw.text {
    Font = model.DiagFont
    Text =
      $"FPS: {model.Diag.Fps}  ({model.Diag.FrameTime:F1}ms)  Chunks: {model.Chunks.Chunks.Count}  Score: {model.Physics.Score}"
    Position = Vector2(10.0f, 10.0f)
    FontSize = 20.0f
    Spacing = 1.0f
    Color = Color.Yellow
    Layer = 0<RenderLayer>
  }
  |> Draw.text {
    Font = model.DiagFont
    Text =
      $"Time: {model.DayNight.TimeOfDay:F1}h  Pos: ({pos.X:F0},{pos.Y:F0},{pos.Z:F0})  Grounded: {model.Physics.IsGrounded}  Particles: {model.Particles.Count}"
    Position = Vector2(10.0f, 35.0f)
    FontSize = 20.0f
    Spacing = 1.0f
    Color = Color.Yellow
    Layer = 0<RenderLayer>
  }
  |> Draw.drop
