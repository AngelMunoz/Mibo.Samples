module Platformer.MonoGame.MinimapView

open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
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
      .sprite(
        SpriteState.create(
          model.MinimapTexture,
          Rectangle(
            int minimapX,
            int minimapY,
            int minimapSize,
            int minimapSize
          ),
          Rectangle(0, 0, texSize, texSize)
        )
        |> SpriteState.withLayer 1010<RenderLayer>
      )
      .drop()

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap

  // XNA palette bytes (XNA Yellow 255,255,0 — Mibo.Color's presets differ)
  let markerColor = Color.rgb 255uy 255uy 0uy

  buffer
    .fillCircle(
      System.Numerics.Vector2(centerX, centerY),
      3.0f,
      markerColor,
      1012<RenderLayer>
    )
    .lineThick(
      System.Numerics.Vector2(centerX, centerY),
      System.Numerics.Vector2(centerX + model.Physics.Facing * 10.0f, centerY),
      markerColor,
      thickness = 2.0f,
      layer = 1012<RenderLayer>
    )
    .rectOutline(
      minimapX,
      minimapY,
      minimapSize,
      minimapSize,
      Color.White,
      thickness = 2.0f,
      layer = 1013<RenderLayer>
    )
    .drop()
