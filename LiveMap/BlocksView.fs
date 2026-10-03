module LiveMap.BlocksView

open System
open System.Numerics
open LiveMap
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Layout
open Mibo.Layout3D
open Mibo.Markup
open Raylib_cs

/// The instanced draw context for block maps.
///
/// Cells group by model name, so every cell that paints the same model
/// leaves in one draw call; `getTransform` states where each instance
/// stands and how tall it is. The context owns pooled transform arrays, so
/// the frame resets it before use.
let contextFor
  (assets: Assets.Assets)
  : InstancedRenderContext<BlockCell, string> =
  let parts(cell: BlockCell) =
    match assets.Models.TryGetValue cell.Model.Name with
    | true, loaded -> loaded.Parts
    | false, _ -> Array.empty

  let transform (basePos: Vector3) (cell: BlockCell) =
    // One cell of footprint in XZ and `Height` cells of column in Y, with
    // the base `Lift` cells above the plane: the mesh stands on y = 0 and
    // is centred on its own origin, so the scale rides in front of a
    // translation to the cell's centre.
    let scaleX = Constants.cellSize / cell.Model.SizeX
    let scaleZ = Constants.cellSize / cell.Model.SizeZ
    let scaleY = cell.Height / cell.Model.SizeY

    let scale = Raymath.MatrixScale(scaleX, scaleY, scaleZ)

    let place =
      Raymath.MatrixTranslate(
        basePos.X + Constants.cellSize * 0.5f,
        basePos.Y + cell.Lift,
        basePos.Z + Constants.cellSize * 0.5f
      )

    Raymath.MatrixMultiply(scale, place)

  InstancedRenderContext<BlockCell, string>(
    getKey = (fun cell -> cell.Model.Name),
    getMeshesAndMaterial = parts,
    getTransform = transform
  )

/// The top of the ground at a cell, in cells above the plane: where a
/// hover outline sits, so it is not buried inside the terrain.
let private groundTop
  (ground: CellGrid2D<BlockCell>)
  (x: int)
  (y: int)
  : float32 =
  match CellGrid2D.get x y ground with
  | ValueSome cell -> cell.Height
  | ValueNone -> 0f

/// The four segments that outline a cell rectangle, a little above the
/// surface of the ground they stand on.
let private cellOutline
  (surface: float32)
  (x: int)
  (y: int)
  (w: int)
  (h: int)
  : struct (Vector3 * Vector3)[] =
  [|
    let x0 = float32 x * Constants.cellSize
    let z0 = float32 y * Constants.cellSize
    let x1 = float32(x + w) * Constants.cellSize
    let z1 = float32(y + h) * Constants.cellSize
    let y = surface + 0.35f
    struct (Vector3(x0, y, z0), Vector3(x1, y, z0))
    struct (Vector3(x1, y, z0), Vector3(x1, y, z1))
    struct (Vector3(x1, y, z1), Vector3(x0, y, z1))
    struct (Vector3(x0, y, z1), Vector3(x0, y, z0))
  |]

/// Draws what the pointer is over, as an outline on the ground.
let private drawHover
  (ground: CellGrid2D<BlockCell>)
  (hover: Hover.Info voption)
  (buffer: RenderBuffer3D)
  : unit =
  hover
  |> ValueOption.iter(fun info ->
    let struct (x, y) = info.Cell

    for struct (start, finish) in cellOutline (groundTop ground x y) x y 1 1 do
      buffer.line3D(start, finish, Color.White) |> ignore

    info.Rect
    |> ValueOption.iter(fun region ->
      for struct (start, finish) in
        cellOutline
          (groundTop ground region.X region.Y)
          region.X
          region.Y
          region.W
          region.H do
        buffer.line3D(start, finish, (Color.create 255uy 214uy 102uy 255uy))
        |> ignore))

/// The block pass: every layer of the map bottom first, lit by one
/// directional light.
///
/// Each grid carries the lift the build gave it, so an upper layer stands
/// on the ground the layers below reach at that cell instead of replacing
/// it: the terrain stays whole, and a hut's platform floor sits on the
/// field rather than sinking into it.
let view
  (context: InstancedRenderContext<BlockCell, string>)
  (camera: Camera3D)
  (drawn: CellGrid2D<BlockCell>[])
  (layers: DocFlow.BuiltLayer<BlockCell>[])
  (hover: Hover.Info voption)
  (buffer: RenderBuffer3D)
  : unit =
  context.ResetFrameBuffers()

  buffer
    .beginCamera(camera, Constants.mapLayer)
    .setAmbientLight(
      {
        Color = Color.create 150uy 150uy 160uy 255uy
        Intensity = 0.55f
      }
    )
    .addDirectionalLight(
      {
        Direction = Vector3(-0.45f, -1.0f, -0.35f)
        Color = Color.create 255uy 244uy 214uy 255uy
        Intensity = 1.1f
        CastsShadows = false
      }
    )
    .drop()

  for grid in drawn do
    context.RenderInstanced(buffer, grid)

  if layers.Length > 0 then
    // every layer shares the map's cell geometry, and the bottom grid is
    // the terrain the hover outlines stand on
    drawHover layers[0].Grid hover buffer

  buffer.endCamera(Constants.overlayLayer).drop()
