module MonoGL.View

open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open PingPong.Shared.Types

let view (_ctx: GameContext) (model: GameState) (buffer: RenderBuffer2D) =
  buffer
    .fillRect(
      0f,
      model.LeftPaddle.Y - paddleHeight / 2f,
      paddleWidth,
      paddleHeight,
      Color.White
    )
    .drop()

  buffer
    .fillRect(
      model.Width - paddleWidth,
      model.RightPaddle.Y - paddleHeight / 2f,
      paddleWidth,
      paddleHeight,
      Color.White
    )
    .drop()

  buffer
    .fillCircle(
      Vector2(model.Ball.Position.X, model.Ball.Position.Y),
      ballRadius,
      Color.White
    )
    .drop()

  for y in 0.0f .. 20.0f .. model.Height do
    buffer
      .fillRect(model.Width / 2f - 1f, y, 2f, 10f, Color.rgb 130uy 130uy 130uy)
      .drop()
