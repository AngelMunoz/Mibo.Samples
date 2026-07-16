module Platformer3D.MonoGame.MinimapView

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Platformer3D.Minimap
open Platformer3D.MonoGame.Types

[<Literal>]
let private texSize = 200

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let screenWidth = float32 ctx.WindowWidth
  let screenHeight = float32 ctx.WindowHeight

  let minimapX = int(screenWidth - minimapSize - minimapMargin)
  let minimapY = int(screenHeight - minimapSize - minimapMargin)
  let halfMinimap = minimapSize * 0.5f

  if model.MinimapTexReady then
    buffer
    |> Draw.sprite(
      SpriteState.create(
        model.MinimapTexture,
        Microsoft.Xna.Framework.Rectangle(
          minimapX,
          minimapY,
          int minimapSize,
          int minimapSize
        ),
        Microsoft.Xna.Framework.Rectangle(0, 0, texSize, texSize)
      )
      |> SpriteState.withLayer 100<RenderLayer>
    )
    |> Draw.drop

  let centerX = float32 minimapX + halfMinimap
  let centerY = float32 minimapY + halfMinimap
  let facingX = sin model.Physics.Facing
  let facingZ = cos model.Physics.Facing

  buffer
  |> Draw.fillCircle
    (102<RenderLayer>, Microsoft.Xna.Framework.Color.Yellow)
    (Vector2(centerX, centerY), 3.0f)
  |> Draw.lineThick
    (102<RenderLayer>, Microsoft.Xna.Framework.Color.Yellow, 2.0f)
    (Vector2(centerX, centerY),
     Vector2(centerX + facingX * 10.0f, centerY + facingZ * 10.0f))
  |> Draw.rectOutline
    (103<RenderLayer>, Microsoft.Xna.Framework.Color.White, 2.0f)
    (Microsoft.Xna.Framework.Rectangle(
      minimapX,
      minimapY,
      int minimapSize,
      int minimapSize
    ))
  |> Draw.drop
