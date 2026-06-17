module MonoClient.View

open System.Numerics
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open PingPong.Shared.Types

let view (_ctx: GameContext) (model: GameState) (buffer: RenderBuffer2D) =
  buffer
  |> Draw.fillRect
    (0<RenderLayer>, Color.White)
    (Rectangle(
      0,
      int(model.LeftPaddle.Y - paddleHeight / 2f),
      int paddleWidth,
      int paddleHeight
    ))
  |> Draw.fillRect
    (0<RenderLayer>, Color.White)
    (Rectangle(
      int(model.Width - paddleWidth),
      int(model.RightPaddle.Y - paddleHeight / 2f),
      int paddleWidth,
      int paddleHeight
    ))
  |> Draw.fillCircle
    (0<RenderLayer>, Color.White)
    (Vector2(model.Ball.Position.X, model.Ball.Position.Y), ballRadius)
  |> ignore

  for y in 0.0f .. 20.0f .. model.Height do
    buffer
    |> Draw.fillRect
      (0<RenderLayer>, Color.Gray)
      (Rectangle(int(model.Width / 2f - 1f), int y, 2, 10))
    |> ignore
