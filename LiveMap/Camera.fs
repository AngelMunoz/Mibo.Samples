module LiveMap.Camera

open System
open System.Numerics
open LiveMap
open Mibo.Elmish
open Raylib_cs

/// The 2D view: the world point at the centre of the screen and how many
/// screen pixels one world pixel takes.
[<Struct>]
type FlatState = { Target: Vector2; Zoom: float32 }

/// The 3D view: an orbit around a point on the ground plane, the angle
/// the view is seen from, and how far away it stands.
[<Struct>]
type BlockState = {
  Target: Vector3
  /// The orbit angle in radians. Q and E turn it; the pitch stays fixed,
  /// so a document always reads as a map and not as a horizon.
  Yaw: float32
  Distance: float32
}

type Model = {
  Flat: FlatState
  Blocks: BlockState
  /// The window size, so a camera maps screen points without asking the
  /// window.
  Viewport: Vector2
}

[<Struct>]
type Msg =
  /// One frame of held input: the pan direction (one unit per axis, +Y is
  /// up-screen), the zoom direction (+1 zooms in), the rotate direction
  /// (+1 turns the 3D view right), and the frame time.
  | Move of pan: Vector2 * zoom: float32 * rotate: float32 * dt: float32
  /// Puts the map in the middle of the view and fits it, for the first
  /// frame of a document and for a mode change.
  | Frame of columns: int * rows: int
  | Reset

/// Keeps a value inside a range.
let inline private within (low: float32) (high: float32) (value: float32) =
  min high (max low value)

/// Folds an angle into one turn, so a held rotate key cannot grow the
/// angle without bound.
let inline private wrapAngle(angle: float32) : float32 =
  let turn = 2.0f * MathF.PI
  angle - turn * MathF.Floor(angle / turn)

let private initialFlat: FlatState = { Target = Vector2.Zero; Zoom = 1.0f }

let private initialBlocks: BlockState = {
  Target = Vector3.Zero
  Yaw = Constants.orbitYaw
  Distance = Constants.orbitDistance
}

let init(viewport: Vector2) : Model = {
  Flat = initialFlat
  Blocks = initialBlocks
  Viewport = viewport
}

/// The raylib camera the flat pass draws through.
let flat(model: Model) : Camera2D =
  Camera2D.create model.Flat.Target model.Flat.Zoom model.Viewport

/// The raylib camera the block pass draws through.
let blocks(model: Model) : Camera3D =
  Camera3D.orbit
    model.Blocks.Target
    model.Blocks.Yaw
    Constants.orbitPitch
    model.Blocks.Distance
    45.0f

let private moveFlat
  (pan: Vector2)
  (zoom: float32)
  (dt: float32)
  (state: FlatState)
  : FlatState =
  // the pan is stated in cells on screen, so the arrows travel the same
  // distance whatever the zoom is
  let step =
    Constants.panCellsPerSecond * Constants.cellPixels * dt / state.Zoom

  let factor = exp(Constants.zoomPerSecond * dt * zoom)

  {
    // the intent states +Y as up-screen and world Y grows down, so only
    // that axis flips: Right moves the target right, Up moves it up
    Target = state.Target + Vector2(pan.X, -pan.Y) * step
    Zoom = within Constants.minZoom Constants.maxZoom (state.Zoom * factor)
  }

let private moveBlocks
  (pan: Vector2)
  (zoom: float32)
  (rotate: float32)
  (dt: float32)
  (state: BlockState)
  : BlockState =
  // the arrows move along the screen axes of the current view: with a
  // turned orbit, moving along the world axes would send Right up and to
  // the left
  let yaw = state.Yaw
  let right = Vector2(MathF.Cos yaw, -MathF.Sin yaw)
  let forward = Vector2(-MathF.Sin yaw, -MathF.Cos yaw)
  let step = Constants.orbitPanUnitsPerSecond * dt
  let moved = (right * pan.X + forward * pan.Y) * step

  {
    Target = state.Target + Vector3(moved.X, 0.0f, moved.Y)
    Yaw = wrapAngle(yaw + Constants.orbitYawPerSecond * dt * rotate)
    Distance =
      within
        Constants.minOrbitDistance
        Constants.maxOrbitDistance
        (state.Distance * exp(-Constants.zoomPerSecond * dt * zoom))
  }

let private frame (columns: int) (rows: int) (model: Model) : Model =
  let columns = float32(max columns 1)
  let rows = float32(max rows 1)

  let flat: FlatState = {
    Target =
      Vector2(
        columns * Constants.cellPixels * 0.5f,
        rows * Constants.cellPixels * 0.5f
      )
    Zoom = 1.0f
  }

  let blocks: BlockState = {
    Target =
      Vector3(
        columns * Constants.cellSize * 0.5f,
        0.0f,
        rows * Constants.cellSize * 0.5f
      )
    // framing moves the view, it does not turn it
    Yaw = model.Blocks.Yaw
    Distance =
      within
        Constants.minOrbitDistance
        Constants.maxOrbitDistance
        (max columns rows * Constants.cellSize * 1.6f)
  }

  {
    model with
        Flat = flat
        Blocks = blocks
  }

/// Applies one camera message.
let update (msg: Msg) (model: Model) : Model =
  match msg with
  | Move(pan, zoom, rotate, dt) -> {
      model with
          Flat = moveFlat pan zoom dt model.Flat
          Blocks = moveBlocks pan zoom rotate dt model.Blocks
    }
  | Frame(columns, rows) -> frame columns rows model
  | Reset ->
      {
        model with
            Flat = initialFlat
            Blocks = initialBlocks
      }
