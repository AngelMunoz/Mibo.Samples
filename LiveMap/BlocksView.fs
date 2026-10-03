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
/// stands, how tall it is, and how many cells it covers. The context owns
/// pooled transform arrays, so the frame resets it before use.
let contextFor
  (assets: Assets.Assets)
  : InstancedRenderContext<BlockCell, string> =
  let parts(cell: BlockCell) =
    match assets.Models.TryGetValue cell.Model.Name with
    | true, loaded -> loaded.Parts
    | false, _ -> Array.empty

  let transform (rect: CellRect) (basePos: Vector3) (cell: BlockCell) =
    // The instance fills the rectangle it covers in XZ and `Height` cells of
    // column in Y, with the base `Lift` cells above the plane: the mesh stands
    // on y = 0 and is centred on its own origin, so the scale rides in front of
    // a translation to the middle of the rectangle. `rect.H` is the depth, and
    // the anchor cell's world position is the rectangle's near corner.
    let boxW = float32 rect.W * Constants.cellSize
    let boxD = float32 rect.H * Constants.cellSize

    let scaleX = boxW / cell.Model.SizeX
    let scaleZ = boxD / cell.Model.SizeZ
    let scaleY = cell.Height / cell.Model.SizeY

    let scale = Raymath.MatrixScale(scaleX, scaleY, scaleZ)

    let place =
      Raymath.MatrixTranslate(
        basePos.X + boxW * 0.5f,
        basePos.Y + cell.Lift,
        basePos.Z + boxD * 0.5f
      )

    Raymath.MatrixMultiply(scale, place)

  InstancedRenderContext<BlockCell, string>
    .Rect(
      getKey = (fun cell -> cell.Model.Name),
      getMeshesAndMaterial = parts,
      getTransform = transform
    )

/// The top of the stack at a cell, in cells above the plane: where a hover
/// outline sits, so it is not buried inside the surface it names. The
/// topmost layer that owns the cell answers, and a plate answers for every
/// cell it covers.
let private stackTop
  (layers: DocFlow.BuiltLayer<BlockCell>[])
  (drawn: CellGrid2D<BlockCell>[])
  (x: int)
  (y: int)
  : float32 =
  let mutable top = 0f
  let mutable found = false
  let mutable i = layers.Length - 1

  while not found && i >= 0 do
    (match Occupancy.owner x y layers[i].Occupancy with
     | ValueSome at ->
       (match CellGrid2D.get at.X at.Y drawn[i] with
        | ValueSome cell ->
          top <- cell.Lift + cell.Height
          found <- true
        | ValueNone -> ())
     | ValueNone -> ())

    i <- i - 1

  top

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

/// Draws what the pointer is over, as outlines on the surface it names: the
/// instance that owns the cell, the cell itself, and the document region
/// that covers it.
let private drawHover
  (layers: DocFlow.BuiltLayer<BlockCell>[])
  (drawn: CellGrid2D<BlockCell>[])
  (hover: Hover.Info voption)
  (buffer: RenderBuffer3D)
  : unit =
  let outline (color: Mibo.Color) (rect: CellRect) =
    for struct (start, finish) in
      cellOutline
        (stackTop layers drawn rect.X rect.Y)
        rect.X
        rect.Y
        rect.W
        rect.H do
      buffer.line3D(start, finish, color) |> ignore

  hover
  |> ValueOption.iter(fun info ->
    let struct (x, y) = info.Cell

    // a plate outlines whole, so the reader sees the instance the cell
    // belongs to
    info.Instance
    |> ValueOption.iter(outline(Mibo.Color.create 120uy 200uy 255uy 255uy))

    outline Mibo.Color.White { X = x; Y = y; W = 1; H = 1 }

    info.Rect
    |> ValueOption.iter(outline(Mibo.Color.create 255uy 214uy 102uy 255uy)))

/// The block pass: every layer of the map bottom first, lit by one
/// directional light.
///
/// Each grid carries the lift the build gave it, so an upper layer stands
/// on the ground the layers below reach at that cell instead of replacing
/// it: the terrain stays whole, and a hut's platform floor sits on the
/// field rather than sinking into it. Every layer draws through its own
/// occupancy, so a word that spans cells leaves one instance stretched over
/// the rectangle it covers.
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

  for i in 0 .. drawn.Length - 1 do
    context.RenderInstanced(buffer, drawn[i], layers[i].Occupancy)

  if layers.Length > 0 && drawn.Length > 0 then
    drawHover layers drawn hover buffer

  buffer.endCamera(Constants.overlayLayer).drop()
