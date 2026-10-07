module Platformer3D.Raylib.MinimapView

open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
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
      .sprite(
        SpriteState.create(
          model.MinimapTexture,
          Rectangle(minimapX, minimapY, minimapSize, minimapSize),
          Rectangle(0.0f, 0.0f, float32 texSize, float32 texSize)
        )
        |> SpriteState.withLayer 100<RenderLayer>
      )
      .drop()

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap
  let facingX = sin model.Physics.Facing
  let facingZ = cos model.Physics.Facing

  // raylib palette bytes (raylib Yellow 253,249,0 — Mibo.Color's presets differ)
  let markerColor = Color.rgb 253uy 249uy 0uy

  buffer
    .fillCircle(Vector2(centerX, centerY), 3.0f, markerColor, 102<RenderLayer>)
    .lineThick(
      Vector2(centerX, centerY),
      Vector2(centerX + facingX * 10.0f, centerY + facingZ * 10.0f),
      markerColor,
      thickness = 2.0f,
      layer = 102<RenderLayer>
    )
    .rectOutline(
      minimapX,
      minimapY,
      minimapSize,
      minimapSize,
      Color.White,
      thickness = 2.0f,
      layer = 103<RenderLayer>
    )
    .drop()
