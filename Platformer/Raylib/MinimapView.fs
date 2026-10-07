module Platformer.Raylib.MinimapView

open System.Numerics
open Raylib_cs
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
          Rectangle(minimapX, minimapY, minimapSize, minimapSize),
          Rectangle(0.0f, 0.0f, float32 texSize, float32 texSize)
        )
        |> SpriteState.withLayer 1010<RenderLayer>
      )
      .drop()

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap

  // raylib palette bytes (raylib Yellow 253,249,0 — Mibo.Color's presets differ)
  let markerColor = Color.rgb 253uy 249uy 0uy

  buffer
    .fillCircle(Vector2(centerX, centerY), 3.0f, markerColor, 1012<RenderLayer>)
    .lineThick(
      Vector2(centerX, centerY),
      Vector2(centerX + model.Physics.Facing * 10.0f, centerY),
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
