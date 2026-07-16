module Platformer3D.Raylib.MinimapView

open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Platformer3D.Minimap
open Platformer3D.Raylib.Types

[<Literal>]
let private texSize = 200

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let minimap = model.Minimap
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
        Rectangle(minimapX, minimapY, minimapSize, minimapSize),
        Rectangle(0.0f, 0.0f, float32 texSize, float32 texSize)
      )
      |> SpriteState.withLayer 100<RenderLayer>
    )
    |> Draw.drop

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap
  let facingX = sin model.Physics.Facing
  let facingZ = cos model.Physics.Facing

  buffer
  |> Draw.fillCircle
    (102<RenderLayer>, Color.Yellow)
    (Vector2(centerX, centerY), 3.0f)
  |> Draw.lineThick
    (102<RenderLayer>, Color.Yellow, 2.0f)
    (Vector2(centerX, centerY),
     Vector2(centerX + facingX * 10.0f, centerY + facingZ * 10.0f))
  |> Draw.rectOutline
    (103<RenderLayer>, Color.White, 2.0f)
    (Rectangle(minimapX, minimapY, minimapSize, minimapSize))
  |> Draw.drop
