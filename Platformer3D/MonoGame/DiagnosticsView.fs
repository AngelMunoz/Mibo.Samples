module Platformer3D.MonoGame.DiagnosticsView

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Platformer3D.MonoGame.Types

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let pos = model.Physics.Position

  let inline writeLine (yPos: float32) (text: string) =
    buffer
    |> Draw.text(
      TextState.create(model.DiagFont, text, Vector2(10.0f, yPos))
      |> TextState.withScale 0.75f
      |> TextState.withColor Microsoft.Xna.Framework.Color.Yellow
      |> TextState.withLayer 0<RenderLayer>
    )
    |> Draw.drop

  writeLine
    30.0f
    $"FPS: {model.Diag.Fps}  Chunks: {model.Chunks.Chunks.Count}  Score: {model.Physics.Score}"

  writeLine
    55.0f
    $"Time: {model.DayNight.TimeOfDay:F1}h  Pos: ({pos.X:F0},{pos.Y:F0},{pos.Z:F0})  Grounded: {model.Physics.IsGrounded}  Particles: {model.Particles.Count}"
