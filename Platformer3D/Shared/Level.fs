/// Platformer3D's level vocabulary — a semantic Flow module, the pattern
/// from Mibo's "Flow - Level Authoring" guide ("a semantic document for
/// your game"). Chunks are authored as Flow documents over 2D footprint
/// grids; the vertical axis never appears in the layout math. It rides in
/// the tile — `TerrainColumn.Height` for ground, `PropTile.Y` for
/// everything floating above it. That is the framework's "3D as 2D plus
/// column height" model, end to end.
///
/// The world is ONE COURSE over the void. Only chunk row 0 holds ground:
/// a narrow lane (7 cells wide) that runs along X and snakes in Z —
/// runways to sprint, budget-checked gaps to jump, staircases where the
/// course climbs two cells, a checkpoint flag per chunk. Lane centers and
/// heights are scheduled per chunk index, so segments chain seamlessly.
/// Every other chunk row generates as empty sky: there is exactly one
/// path, and falling off it means falling into the void.
///
/// The vocabulary, in reading order:
///
///   terrain  `runway`, `gap`, `stairs` are the course pieces, laid as
///            offset lane bands; `Level.segment` composes a chunk's worth
///            and tags each piece ("runway", "gap", "checkpoint",
///            "spawn") so the layers above can read the structure back.
///   props    `hazardPatches` put spikes on the lane, `laneBorders` frame
///            its edges with trees and rocks, `gapPlatforms` float one
///            platform object over some gaps, `checkpointFlag` marks the
///            exit.
///   pickups  `coinArcs` trace the jump line over every gap, `coinLines`
///            run down the lane, `spawnRing` rings the start.
///
/// Everything here runs once per chunk at generation time (the Flow
/// contract: build once, query per frame). Surface shapes beyond the
/// plain Block (Low, Narrow, Hexagon, Large, Slope, ...) stay available
/// in BlockData for hand-authored set pieces; generation keeps every
/// runway flat so the running surface always reads exactly where it is.
module Platformer3D.Level

open System
open System.Collections.Generic
open Mibo.Layout
open Platformer3D.Constants
open Platformer3D.Types

// -------------------------------------------------------------
// Jump reachability — physics-derived, the budget every gap obeys
// -------------------------------------------------------------

/// Height (in cells) the player reaches above the launch surface at
/// Euclidean horizontal distance `d` (in cells), for a fully-held running
/// jump. Derived from the physics constants:
///   gravity=-20, jumpSpeed=12, moveSpeed=8, cellSize=1
///   apex: h=3.6 at d=4.8, max same-level range: d=9.6.
let arcHeight3D(horizontalDist: float32) : float32 =
  let d = horizontalDist * cellSize
  let t = d / moveSpeed
  (jumpSpeed * t + 0.5f * gravity * t * t) / cellSize

/// Maximum same-level gap (in cells) a running jump can clear.
let maxLevelGap3D: float32 =
  (2.0f * moveSpeed * jumpSpeed / abs(gravity)) / cellSize

/// True when a surface `horizontalDist` cells away and `rise` cells higher
/// (negative = lower) is reachable by a fully-held running jump.
let reachable3D (horizontalDist: float32) (rise: float32) : bool =
  arcHeight3D(max horizontalDist 1.0f) >= rise

// -------------------------------------------------------------
// The lane — the course's footprint in Z
// -------------------------------------------------------------

/// Half the lane's width, in cells: the running band is 2*laneHalf+1.
[<Literal>]
let LaneHalf = 3

/// The lane's Z center for chunk `cx`: a deterministic walk from the
/// spawn lane center, drifting at most two cells per chunk so neighboring
/// segments' bands always overlap and the course snakes instead of
/// running as a straight ruler.
let laneCenterAt (worldSeed: int) (cx: int) : int =
  let mutable center = chunkDepth / 2

  for i in 1..cx do
    let h = abs((i * 19349663) ^^^ worldSeed)

    center <-
      Math.Clamp(
        center + ((h % 5) - 2) * 2,
        LaneHalf + 1,
        chunkDepth - LaneHalf - 2
      )

  center

// -------------------------------------------------------------
// Authoring specs — the tuning knobs the documents take
// -------------------------------------------------------------

/// Runway and gap sizing for one course segment.
type SegmentSpec = {
  /// Runway length in cells (clamped to fit the chunk).
  MinRun: int
  MaxRun: int
  /// Gap width in cells; every (gap, rise) pair is checked against the
  /// jump budget before it is committed.
  MinGap: int
  MaxGap: int
}

/// Prop-layer knobs (seeded per chunk).
type PropsSpec = {
  /// Chance a runway gets a spike patch to jump.
  HazardChance: float32
  /// Chance a gap gets a stepping platform above it.
  HopChance: float32
  /// One border decoration every N cells of lane edge.
  BorderSpacing: int
}

/// Pickup-layer knobs (seeded per chunk).
type PickupsSpec = {
  /// Coin lines per runway.
  LinesPerRunway: int
  /// Chance a gap's coin arc is gold instead of silver.
  GoldChance: float32
}

/// The spawn strip's length knob, in cells.
type SpawnArea = { Length: int }

// -------------------------------------------------------------
// Ground helpers
// -------------------------------------------------------------

/// A flat runway column: solid ground, plain Block cap. The surface a
/// player reads and trusts.
let inline private flatGround
  (material: Biome3D)
  (height: int)
  : TerrainColumn =
  {
    Material = material
    Height = height
    Cap = ValueSome Block
  }

/// The surface a prop would stand on, in world cells above the chunk base:
/// the column's ground plus its cap cell. `ValueNone` over pits and outside
/// the grid.
let inline surfaceOf
  (terrain: CellGrid2D<TerrainColumn>)
  (x: int)
  (z: int)
  : int voption =
  match CellGrid2D.get x z terrain with
  | ValueSome col when not(TerrainColumn.isPit col) ->
    ValueSome(TerrainColumn.surfaceCells col)
  | _ -> ValueNone

// -------------------------------------------------------------
// Terrain vocabulary — the course pieces
// -------------------------------------------------------------

/// A flat runway: solid ground at one height, as a lane band at `center`,
/// spanning `length` cells along X. Tagged "runway" so hazards,
/// decoration, and coin lines can find every stretch of runnable ground.
let runway
  (length: int)
  (center: int)
  (material: Biome3D)
  (height: int)
  : Stamp<TerrainColumn> =
  Stamp.offset
    0
    (center - LaneHalf)
    (Stamp.tagged
      [ "runway" ]
      (Stamp.box length (2 * LaneHalf + 1) [
        Flow.fill(flatGround material height)
      ]))

/// A gap: carved void across the lane — the jump. Tagged "gap" so hop
/// platforms and coin arcs can trace it. (Nothing paints: the piece is a
/// landmark marking ground that is deliberately NOT there.)
let gap (length: int) (center: int) : Stamp<TerrainColumn> =
  Stamp.offset
    0
    (center - LaneHalf)
    (Stamp.tagged
      [ "gap" ]
      (Stamp.box length (2 * LaneHalf + 1) [ Flow.fill TerrainColumn.pit ]))

/// A staircase runway: the lane band's ground climbs (or descends) one
/// cell every two cells of run, landing at `target` by its far end. Used
/// where the course steps two cells — one-cell steps are plain jumps.
let stairs
  (length: int)
  (center: int)
  (material: Biome3D)
  (fromHeight: int)
  (target: int)
  : Stamp<TerrainColumn> =
  let rise = target - fromHeight
  let dir = Math.Sign rise

  Stamp.offset
    0
    (center - LaneHalf)
    (Stamp.tagged
      [ "runway" ]
      (Stamp.box length (2 * LaneHalf + 1) [
        Flow.texture(fun x _ ->
          let climbed = min (abs rise) (x / 2)
          flatGround material (fromHeight + dir * climbed))
      ]))

/// The widest gap a running jump clears for the given rise.
let private payableGap (rise: int) (maxGap: int) (minGap: int) =
  let mutable w = maxGap

  while w > minGap && not(reachable3D (float32 w) (float32 rise)) do
    w <- w - 1

  w

/// One chunk's course: lane-band runways separated by gaps, entering at
/// `entryHeight`/`entryCenter` and leaving at `exitHeight`/`exitCenter`.
/// Heights move by one or two cells between strips (a two-cell step
/// becomes a staircase); the lane center walks toward the exit center in
/// steps small enough that consecutive bands overlap. Every gap width
/// shrinks until its rise is inside the jump budget. The spawn chunk
/// opens with the named spawn strip; every chunk closes with its tagged
/// checkpoint strip at exactly the next chunk's entry band, so courses
/// chain seamlessly across seams.
let segment
  (rng: Random)
  (spec: SegmentSpec)
  (material: Biome3D)
  (entryHeight: int)
  (exitHeight: int)
  (entryCenter: int)
  (exitCenter: int)
  (spawn: SpawnArea voption)
  : Stamp<TerrainColumn> =
  // The height walk: entry -> m1 -> m2 -> exit, each step -2..2, biased
  // toward the exit so the course always gets there.
  let stepToward (from: int) (target: int) =
    let delta = Math.Clamp(target - from, -2, 2)

    match delta with
    | 0 -> from
    | _ ->
      // A 2-step usually becomes a staircase; sometimes leave it as one
      // hard jump by taking only half of it here (the exit strip still
      // collects the remainder).
      if abs delta = 2 && rng.Next 10 < 4 then
        from + Math.Sign delta
      else
        from + delta

  // The lane walk: two Z steps from entry to exit center, each <= 2, so
  // consecutive bands (7 wide) always overlap.
  let laneStep =
    let delta = exitCenter - entryCenter
    Math.Clamp(delta / 2, -2, 2)

  let center1 = entryCenter + laneStep
  let center2 = exitCenter // the checkpoint band must match the neighbor

  // Pieces are placed at explicit (x, z) offsets — the segment tracks the
  // X cursor itself, the bands carry their own Z.
  let pieces = ResizeArray<Stamp<TerrainColumn>>()
  let mutable used = 0

  let addPiece (stamp: Stamp<TerrainColumn>) (length: int) =
    pieces.Add(Stamp.offset used 0 stamp)
    used <- used + length

  // The checkpoint strip is RESERVED before any middle piece runs: the
  // last runway always keeps its cells, whatever the seed rolls. Without
  // this, a long early run could push the exit off the chunk and leave
  // bare void at the seam — the "platform you just fall down" bug.
  let exitReserve = spec.MaxGap + 4

  let addRun
    (maxLen: int)
    (length: int)
    (center: int)
    (height: int)
    (fromHeight: int)
    =
    let length = min (max length spec.MinRun) maxLen

    if length >= 2 then
      let rise = height - fromHeight

      let strip =
        if rise = 2 || rise = -2 then
          stairs length center material fromHeight height
        else
          runway length center material height

      addPiece strip length

  // Opening: the named spawn strip on the spawn chunk, a plain runway
  // otherwise.
  match spawn with
  | ValueSome area ->
    let length = Math.Clamp(area.Length, 4, spec.MaxRun)
    let strip = runway length entryCenter material entryHeight

    pieces.Add(
      Stamp.offset 0 0 (Stamp.named "spawn" (Stamp.tagged [ "safe" ] strip))
    )

    used <- used + length
  | ValueNone ->
    addRun
      chunkWidth
      (rng.Next(spec.MinRun, spec.MaxRun + 1))
      entryCenter
      entryHeight
      entryHeight

  // Middle strips via m1/m2, each behind a budget-checked gap, never
  // eating into the reserved exit.
  let mutable previous = entryHeight
  let mutable center = entryCenter

  for height, nextCenter in
    [ (stepToward entryHeight exitHeight, center1); (exitHeight, center2) ] do
    let height = Math.Clamp(height, 1, 6)
    let room = chunkWidth - used - exitReserve

    if room >= spec.MinGap + spec.MinRun then
      let gapWidth =
        min
          (payableGap (height - previous) spec.MaxGap spec.MinGap)
          (room - spec.MinRun)

      if gapWidth >= spec.MinGap then
        addPiece (gap gapWidth center) gapWidth

      addRun
        (chunkWidth - used - exitReserve)
        (rng.Next(spec.MinRun, spec.MaxRun + 1))
        nextCenter
        height
        previous

      previous <- height
      center <- nextCenter

  // Exit: the checkpoint gap, then the reserved checkpoint strip filling
  // the remaining cells — always at least four cells of solid lane.
  let room = chunkWidth - used

  if room > spec.MinGap + 4 then
    let gapWidth =
      min
        (payableGap (exitHeight - previous) spec.MaxGap spec.MinGap)
        (room - 4)

    if gapWidth >= spec.MinGap then
      addPiece (gap gapWidth center) gapWidth

  let exitLength = chunkWidth - used

  if exitLength >= 2 then
    addPiece
      (Stamp.tagged
        [ "checkpoint" ]
        (runway exitLength exitCenter material exitHeight))
      exitLength

  Flow.overlay(Seq.toList pieces)

/// A chunk with no course in it: empty sky. Every grid stays empty, so
/// column lookups report no ground, no props, no pickups.
let emptyChunkMarks() : Landmarks = {
  Width = chunkWidth
  Height = chunkDepth
  Named = Dictionary()
  Tagged = Dictionary()
  Cells = Dictionary()
}

// -------------------------------------------------------------
// Prop vocabulary — set pieces placed from the course's landmarks
// -------------------------------------------------------------

/// Spike patches on the runways: a short strip of spikes mid-lane to jump
/// on the way through. Never on the spawn ("safe") or checkpoint strips —
/// the start and the goal stay clean.
let hazardPatches
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (spec: PropsSpec)
  : Stamp<PropTile> =
  Stamp.tagged
    [ "hazard" ]
    (Flow.canvas [
      fun section ->
        for rect in Flow.taggedRects "runway" marks do
          let at = { X = rect.X; Y = rect.Y }

          if
            not(Flow.isTag "safe" at marks)
            && not(Flow.isTag "checkpoint" at marks)
            && float32(rng.NextDouble()) < spec.HazardChance
          then
            let patchX = rect.X + 2 + rng.Next(max 1 (rect.W - 4))
            let patchZ = rect.Y + rect.H / 2 + rng.Next(-2, 3)
            let wide = rng.Next 4 = 0
            let length = if wide then 2 else 1

            for dx = 0 to length - 1 do
              let x = patchX + dx

              match surfaceOf terrain x patchZ with
              | ValueSome surface ->
                let kind =
                  if wide && dx = 0 then
                    HazardKind.SpikesWide
                  else
                    HazardKind.Spikes

                CellGrid2D.set
                  x
                  patchZ
                  { Prop = Hazard kind; Y = surface }
                  section.BackingGrid
              | ValueNone -> ()
    ])

/// Lane borders: trees, rocks, and crates lining the lane's two Z edges,
/// framing where it is safe to run. Snow biomes get snowy trees.
let laneBorders
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (spec: PropsSpec)
  : BoxStyle<PropTile> =
  fun section ->
    let spacing = max 1 spec.BorderSpacing

    for rect in Flow.taggedRects "runway" marks do
      let safe = Flow.isTag "safe" { X = rect.X; Y = rect.Y } marks

      let menu(material: Biome3D) =
        let snowy = material = Biome3D.Snow

        match rng.Next 10 with
        | 0
        | 1
        | 2 ->
          if snowy then
            DecorationKind.TreeSnow
          else
            DecorationKind.TreePine
        | 3
        | 4 -> DecorationKind.Rock
        | 5
        | 6 -> DecorationKind.Crate
        | 7 -> DecorationKind.Barrel
        | _ ->
          if safe then
            DecorationKind.GrassTuft
          else
            DecorationKind.Mushrooms

      for edgeZ in [ rect.Y; rect.Y + rect.H - 1 ] do
        for x = rect.X to rect.X + rect.W - 1 do
          if x % spacing = 0 then
            match CellGrid2D.get x edgeZ terrain with
            | ValueSome col ->
              CellGrid2D.set
                x
                edgeZ
                {
                  Prop = Decoration(menu col.Material)
                  Y = TerrainColumn.surfaceCells col
                }
                section.BackingGrid
            | ValueNone -> ()

/// Gap platforms: ONE platform object per gap — a single scaled instance
/// of the platform asset spanning the gap plus one landing cell on each
/// lip, floating a cell above the jump line. One tile, one instance, no
/// tiled blocks.
let gapPlatforms
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (spec: PropsSpec)
  : Stamp<PropTile> =
  Stamp.tagged
    [ "platforms" ]
    (Flow.canvas [
      fun section ->
        for rect in Flow.taggedRects "gap" marks do
          if float32(rng.NextDouble()) < spec.HopChance then
            let midZ = rect.Y + rect.H / 2

            // The platform floats one cell over the jump line, measured
            // from the higher runway flanking the gap.
            let heightOf(x: int) =
              surfaceOf terrain x midZ |> ValueOption.defaultValue 0

            let flank = max (heightOf(rect.X - 1)) (heightOf(rect.X + rect.W))

            // Anchor at the lip cell before the gap; the object spans the
            // gap plus both landing cells.
            let anchorX = rect.X - 1
            let length = rect.W + 2

            if
              anchorX >= 0
              && anchorX + length <= chunkWidth
              && flank + 1 < chunkHeight
            then
              CellGrid2D.set
                anchorX
                midZ
                {
                  Prop = Platform length
                  Y = flank + 1
                }
                section.BackingGrid
    ])

/// The checkpoint flag on each chunk's exit strip — the visible goal the
/// course segment points at.
let checkpointFlag
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  : BoxStyle<PropTile> =
  fun section ->
    for rect in Flow.taggedRects "checkpoint" marks do
      let x = rect.X + rect.W / 2
      let z = rect.Y + rect.H / 2

      match surfaceOf terrain x z with
      | ValueSome surface ->
        CellGrid2D.set
          x
          z
          {
            Prop = Decoration DecorationKind.Flag
            Y = surface
          }
          section.BackingGrid
      | ValueNone -> ()

/// The chunk's prop document: hazards and hops read the runway and gap
/// rects, borders frame the lane, the flag reads the checkpoint rect.
let props
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (propsSpec: PropsSpec)
  : Stamp<PropTile> =
  Flow.overlay [
    hazardPatches rng terrain marks propsSpec
    gapPlatforms rng terrain marks propsSpec
    Flow.canvas [
      laneBorders rng terrain marks propsSpec
      checkpointFlag terrain marks
    ]
  ]

// -------------------------------------------------------------
// Pickup vocabulary
// -------------------------------------------------------------

/// Coin arcs over the gaps: one coin per cell along the jump line, rising
/// toward the gap's middle — the trace of the jump itself. Gold on lucky
/// gaps, silver otherwise.
let coinArcs
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (spec: PickupsSpec)
  : BoxStyle<PropTile> =
  fun section ->
    let gold = float32(rng.NextDouble()) < spec.GoldChance

    for rect in Flow.taggedRects "gap" marks do
      let midZ = rect.Y + rect.H / 2

      let heightOf(x: int) =
        surfaceOf terrain x midZ |> ValueOption.defaultValue 0

      let baseH = max (heightOf(rect.X - 1)) (heightOf(rect.X + rect.W))

      for dx = -1 to rect.W do
        let x = rect.X + dx
        // The arc: one cell up at the gap's mouth, two over its middle.
        let mid = rect.W / 2
        let overMiddle = if Math.Abs(dx - (mid - 1)) < max 1 mid then 1 else 0
        let y = baseH + 1 + overMiddle

        if y < chunkHeight then
          let kind = if gold then PickupKind.Gold else PickupKind.Silver

          CellGrid2D.set
            x
            midZ
            { Prop = Pickup kind; Y = y }
            section.BackingGrid

/// Coin lines down the lane: short rows of bronze between the borders.
let coinLines
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (spec: PickupsSpec)
  : BoxStyle<PropTile> =
  fun section ->
    for rect in Flow.taggedRects "runway" marks do
      for _ in 1 .. spec.LinesPerRunway do
        let len = min (3 + rng.Next 3) (rect.W - 2)
        let z = rect.Y + rect.H / 2 + rng.Next(-2, 3)
        let z = Math.Clamp(z, rect.Y + 1, rect.Y + rect.H - 2)
        let startX = rect.X + 1 + rng.Next(max 1 (rect.W - len - 1))

        for dx = 0 to len - 1 do
          let x = startX + dx

          match surfaceOf terrain x z with
          | ValueSome surface ->
            CellGrid2D.set
              x
              z
              {
                Prop = Pickup PickupKind.Bronze
                Y = surface + 1
              }
              section.BackingGrid
          | ValueNone -> ()

/// The gold ring at spawn — placed from the spawn landmark the terrain
/// document reported, one coin per surface cell of the ring.
let spawnRing
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  : BoxStyle<PropTile> =
  fun section ->
    match Flow.tryPosition "spawn" marks with
    | ValueSome rect ->
      let cx = rect.X + rect.W / 2
      let cz = rect.Y + rect.H / 2
      let r = max 1 (min rect.W rect.H / 2)

      for dz in -r .. r do
        for dx in -r .. r do
          if dx * dx + dz * dz <= r * r then
            match surfaceOf terrain (cx + dx) (cz + dz) with
            | ValueSome surface ->
              CellGrid2D.set
                (cx + dx)
                (cz + dz)
                {
                  Prop = Pickup PickupKind.Gold
                  Y = surface + 1
                }
                section.BackingGrid
            | ValueNone -> ()
    | ValueNone -> ()

/// The chunk's pickup document: arcs over gaps, lines down the lane, the
/// gold ring at spawn.
let pickups
  (rng: Random)
  (terrain: CellGrid2D<TerrainColumn>)
  (marks: Landmarks)
  (spec: PickupsSpec)
  : Stamp<PropTile> =
  Flow.overlay [
    Flow.canvas [
      coinArcs rng terrain marks spec
      coinLines rng terrain marks spec
      spawnRing terrain marks
    ]
  ]
