namespace Platformer3D

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Layout
open Mibo.Layout3D
open Mibo.Elmish
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.DayNight

module Minimap =

  [<Literal>]
  let minimapSize = 200.0f

  [<Literal>]
  let minimapMargin = 10.0f

  [<Literal>]
  let minimapWorldRadius = 40.0f

  [<Literal>]
  let sampleStep = 2

  [<Literal>]
  let updateInterval = 4

  [<Literal>]
  let private texSize = 200

  let materialColor(material: Biome3D) =
    match material with
    | Grass -> Color.rgb 76uy 153uy 0uy
    | Snow -> Color.rgb 230uy 230uy 230uy

  let propColor(prop: Prop) =
    match prop with
    | Platform _ -> Color.rgb 100uy 100uy 100uy
    | Hazard _ -> Color.rgb 192uy 192uy 192uy
    | Pickup kind ->
      match kind with
      | Gold
      | Key -> Color.rgb 255uy 215uy 0uy
      | Silver -> Color.rgb 192uy 192uy 192uy
      | Bronze -> Color.rgb 205uy 127uy 50uy
      | Jewel -> Color.rgb 0uy 191uy 255uy
      | Heart -> Color.rgb 255uy 0uy 0uy
      | Star -> Color.rgb 255uy 255uy 0uy
    | Decoration kind ->
      match kind with
      | TreePine
      | TreeSnow -> Color.rgb 0uy 100uy 0uy
      | Rock -> Color.rgb 128uy 128uy 128uy
      | Stones -> Color.rgb 160uy 160uy 160uy
      | GrassTuft -> Color.rgb 50uy 120uy 50uy
      | Flowers
      | FlowersTall -> Color.rgb 200uy 80uy 200uy
      | Mushrooms
      | GlowMushroom -> Color.rgb 139uy 69uy 19uy
      | Crate -> Color.rgb 160uy 82uy 45uy
      | Barrel -> Color.rgb 139uy 90uy 43uy
      | Flag -> Color.rgb 255uy 0uy 0uy

  /// One minimap sample per quantized footprint cell, keeping the highest
  /// world entry: terrain surface by material, then props and pickups over
  /// it when they sit higher.
  let collectEntries
    (bounds: BoundingBox)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    (entries: Dictionary<struct (int * int), struct (float32 * Color)>)
    : unit =
    entries.Clear()

    let remember (wx: int) (wz: int) (worldY: float32) (color: Color) =
      let qx = wx / sampleStep * sampleStep
      let qz = wz / sampleStep * sampleStep
      let key = struct (qx, qz)

      match entries.TryGetValue key with
      | true, struct (existingY, _) when existingY >= worldY -> ()
      | _ -> entries[key] <- struct (worldY, color)

    // The same world-space window the retired volume pass used — per-cell
    // culling inside each intersecting chunk.
    let left = int bounds.Min.X
    let top = int bounds.Min.Z
    let right = int bounds.Max.X
    let bottom = int bounds.Max.Z

    for KeyValue(struct (_cx, _cz), chunk) in chunks do
      if
        chunk.Bounds.Max.X >= bounds.Min.X
        && chunk.Bounds.Min.X <= bounds.Max.X
        && chunk.Bounds.Max.Z >= bounds.Min.Z
        && chunk.Bounds.Min.Z <= bounds.Max.Z
      then
        let origin = chunk.Terrain.Origin

        chunk.Terrain
        |> CellGrid2D.iterVisible left top right bottom (fun x z col ->
          if not(TerrainColumn.isPit col) then
            let wx = int origin.X + x
            let wz = int origin.Y + z

            remember
              wx
              wz
              (float32(TerrainColumn.surfaceCells col) * cellSize)
              (materialColor col.Material))

        let rememberProp(grid: CellGrid2D<PropTile>) =
          grid
          |> CellGrid2D.iterVisible left top right bottom (fun x z tile ->
            let wx = int origin.X + x
            let wz = int origin.Y + z

            remember wx wz (float32 tile.Y * cellSize) (propColor tile.Prop))

        rememberProp chunk.Props
        rememberProp chunk.Pickups

  let generateMinimapData
    (playerPos: Vector3)
    (timeOfDay: float32)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    : struct (Color[] * int * int) =
    let scale = minimapSize / (minimapWorldRadius * 2.0f)

    let bounds = {
      Min =
        Vector3(
          playerPos.X - minimapWorldRadius,
          -100.0f,
          playerPos.Z - minimapWorldRadius
        )
      Max =
        Vector3(
          playerPos.X + minimapWorldRadius,
          100.0f,
          playerPos.Z + minimapWorldRadius
        )
    }

    let skyColor = getSkyColor timeOfDay
    let halfMinimap = minimapSize * 0.5f

    let bgColor =
      Color.create
        (byte(float32 skyColor.R * 0.3f))
        (byte(float32 skyColor.G * 0.3f))
        (byte(float32 skyColor.B * 0.3f))
        200uy

    let pixels = Array.create (texSize * texSize) bgColor

    let pixelSize = float32 sampleStep * scale + 1.0f
    let pixelSizeI = max 1 (int pixelSize)

    let fillRect(px: int, py: int, color: Color) =
      let x0 = max 0 px
      let y0 = max 0 py
      let x1 = min texSize (px + pixelSizeI)
      let y1 = min texSize (py + pixelSizeI)

      for yy = y0 to y1 - 1 do
        let row = yy * texSize

        for xx = x0 to x1 - 1 do
          pixels[row + xx] <- color

    let entries = Dictionary<struct (int * int), struct (float32 * Color)>()
    collectEntries bounds chunks entries

    for KeyValue(struct (wx, wz), struct (_, color)) in entries do
      let relX = (float32 wx - playerPos.X) * scale
      let relZ = (float32 wz - playerPos.Z) * scale
      let pixelX = int(halfMinimap + relX)
      let pixelZ = int(halfMinimap + relZ)

      if
        pixelX >= -pixelSizeI
        && pixelX < texSize
        && pixelZ >= -pixelSizeI
        && pixelZ < texSize
      then
        if color.A > 0uy then
          fillRect(pixelX, pixelZ, color)

    struct (pixels, texSize, texSize)

// -------------------------------------------------------------
// Minimap Sub-system (backend-agnostic)
// -------------------------------------------------------------

module MinimapSystem =

  type MinimapModel() =
    member val FrameCounter = 0 with get, set
    member val LastPlayerPos = Constants.spawnPosition with get, set

  let init() = MinimapModel()

  [<Struct>]
  type MinimapMsg = MinimapReady of colors: Color[] * width: int * height: int

  let update
    (playerPos: Vector3)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    (timeOfDay: float32)
    (model: MinimapModel)
    : struct (MinimapModel * Cmd<MinimapMsg>) =
    let posDelta = playerPos - model.LastPlayerPos

    let needsUpdate =
      model.FrameCounter % Minimap.updateInterval = 0
      || posDelta.LengthSquared() > 4.0f

    model.FrameCounter <- model.FrameCounter + 1

    if needsUpdate then
      model.LastPlayerPos <- playerPos

      let cmd =
        Cmd.ofAsync
          (async {
            return Minimap.generateMinimapData playerPos timeOfDay chunks
          })
          (fun struct (colors, w, h) -> MinimapReady(colors, w, h))
          (fun _ex -> MinimapReady([| Color.Black |], 1, 1))

      struct (model, cmd)
    else
      struct (model, Cmd.none)
