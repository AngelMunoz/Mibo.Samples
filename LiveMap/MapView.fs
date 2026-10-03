module LiveMap.MapView

open System.Numerics
open LiveMap
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Mibo.Markup
open Raylib_cs

/// The world rectangle the camera sees, in world pixels.
let private visibleBounds (camera: Camera2D) (viewport: Vector2) : Rectangle =
  let halfWidth = viewport.X * 0.5f / camera.Zoom
  let halfHeight = viewport.Y * 0.5f / camera.Zoom

  Rectangle(
    camera.Target.X - halfWidth,
    camera.Target.Y - halfHeight,
    halfWidth * 2.0f,
    halfHeight * 2.0f
  )

/// One cell's world rectangle.
let private cellRect (grid: CellGrid2D<'T>) (x: int) (y: int) : Rectangle =
  let world = CellGrid2D.getWorldPos x y grid
  Rectangle(world.X, world.Y, grid.CellSize.X, grid.CellSize.Y)

/// Draws the cells the camera sees, one sprite each.
///
/// `iterVisible` walks only the cells inside the camera's world bounds, so
/// the cost of a frame follows the window and not the size of the map.
/// `index` is the map layer's position in the stack: it becomes the render
/// layer, so an upper grid draws over the one below it.
let private drawCells
  (sheet: Texture2D)
  (grid: CellGrid2D<Cell>)
  (index: int)
  (bounds: Rectangle)
  (buffer: RenderBuffer2D)
  : unit =
  let layer = Constants.mapLayerOf index

  let draw (x: int) (y: int) (cell: Cell) =
    let destination = cellRect grid x y

    let source =
      Rectangle(
        float32 cell.Tile.X,
        float32 cell.Tile.Y,
        float32 cell.Tile.Width,
        float32 cell.Tile.Height
      )

    buffer
      .sprite(
        SpriteState.create(sheet, destination, source)
        |> SpriteState.withLayer layer
      )
      .drop()

  CellGrid2D.iterVisible
    (int bounds.X)
    (int bounds.Y)
    (int(bounds.X + bounds.Width))
    (int(bounds.Y + bounds.Height))
    draw
    grid

/// Draws what the pointer is over: the cell, and the region the document
/// reported for it.
///
/// The region outline is the point of keeping the landmarks. The cell says
/// what it is; the region says which element of the document put it there,
/// so both are optional and the draws simply skip what is absent.
let private drawHover
  (grid: CellGrid2D<Cell>)
  (hover: Hover.Info voption)
  (buffer: RenderBuffer2D)
  : unit =
  hover
  |> ValueOption.iter(fun info ->
    let struct (x, y) = info.Cell
    let cell = cellRect grid x y

    buffer
      .fillRect(
        cell.X,
        cell.Y,
        cell.Width,
        cell.Height,
        Color.create 255uy 255uy 255uy 60uy,
        layer = Constants.hoverFillLayer
      )
      .rectOutline(
        cell.X,
        cell.Y,
        cell.Width,
        cell.Height,
        Color.White,
        thickness = 2.0f,
        layer = Constants.hoverCellLayer
      )
      .drop()

    info.Rect
    |> ValueOption.iter(fun region ->
      let origin = grid.Origin

      let rect =
        Rectangle(
          origin.X + float32 region.X * grid.CellSize.X,
          origin.Y + float32 region.Y * grid.CellSize.Y,
          float32 region.W * grid.CellSize.X,
          float32 region.H * grid.CellSize.Y
        )

      buffer
        .rectOutline(
          rect.X,
          rect.Y,
          rect.Width,
          rect.Height,
          Color.create 255uy 214uy 102uy 255uy,
          thickness = 2.0f,
          layer = Constants.hoverRegionLayer
        )
        .drop()))

/// The flat pass: every layer of the map bottom first, then the hover
/// highlight, in world space.
///
/// An upper layer's grid holds `ValueNone` where nothing painted, and
/// `iterVisible` skips empty cells, so the layer under it shows through
/// with no blending work and no clear-color halo.
let view
  (assets: Assets.Assets)
  (camera: Camera2D)
  (viewport: Vector2)
  (layers: DocFlow.BuiltLayer<Cell>[])
  (hover: Hover.Info voption)
  (buffer: RenderBuffer2D)
  : unit =
  if layers.Length > 0 then
    let bounds = visibleBounds camera viewport

    buffer.beginCamera(camera, Constants.mapLayer).drop()

    for i in 0 .. layers.Length - 1 do
      drawCells assets.Sheet layers[i].Grid i bounds buffer

    // every layer shares the map's cell geometry, so the bottom grid
    // converts the hover's cell coordinates to pixels
    drawHover layers[0].Grid hover buffer

    buffer.endCamera(Constants.overlayLayer).drop()
