module Platformer3D.MonoGame.MinimapView

open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
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
      .sprite(
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
      .drop()

  let centerX = float32 minimapX + halfMinimap
  let centerY = float32 minimapY + halfMinimap
  let facingX = sin model.Physics.Facing
  let facingZ = cos model.Physics.Facing

  // XNA palette bytes (XNA Yellow 255,255,0 — Mibo.Color's presets differ)
  let markerColor = Color.rgb 255uy 255uy 0uy

  buffer
    .fillCircle(
      System.Numerics.Vector2(centerX, centerY),
      3.0f,
      markerColor,
      102<RenderLayer>
    )
    .lineThick(
      System.Numerics.Vector2(centerX, centerY),
      System.Numerics.Vector2(
        centerX + facingX * 10.0f,
        centerY + facingZ * 10.0f
      ),
      markerColor,
      thickness = 2.0f,
      layer = 102<RenderLayer>
    )
    .rectOutline(
      float32 minimapX,
      float32 minimapY,
      minimapSize,
      minimapSize,
      Color.White,
      thickness = 2.0f,
      layer = 103<RenderLayer>
    )
    .drop()
