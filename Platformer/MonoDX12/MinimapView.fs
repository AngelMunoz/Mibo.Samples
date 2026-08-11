module Platformer.MonoGame.MinimapView

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

type Model = Types.Model

[<Literal>]
let minimapSize = 200.0f

[<Literal>]
let minimapMargin = 10.0f

[<Literal>]
let private texSize = 200

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let screenWidth = float32 ctx.WindowWidth
  let screenHeight = float32 ctx.WindowHeight
  let minimapX = screenWidth - minimapSize - minimapMargin
  let minimapY = screenHeight - minimapSize - minimapMargin
  let halfMinimap = minimapSize * 0.5f

  if model.MinimapTexReady then
    buffer
    |> Draw.sprite(
      SpriteState.create(
        model.MinimapTexture,
        Rectangle(int minimapX, int minimapY, int minimapSize, int minimapSize),
        Rectangle(0, 0, texSize, texSize)
      )
      |> SpriteState.withLayer 1010<RenderLayer>
    )
    |> Draw.drop

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap

  buffer
  |> Draw.fillCircle
    (1012<RenderLayer>, Color.Yellow)
    (Vector2(centerX, centerY), 3.0f)
  |> Draw.lineThick
    (1012<RenderLayer>, Color.Yellow, 2.0f)
    (Vector2(centerX, centerY),
     Vector2(centerX + model.Physics.Facing * 10.0f, centerY))
  |> Draw.rectOutline
    (1013<RenderLayer>, Color.White, 2.0f)
    (Rectangle(int minimapX, int minimapY, int minimapSize, int minimapSize))
  |> Draw.drop
