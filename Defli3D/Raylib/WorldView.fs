namespace Defli3D.Raylib

open System
open System.Numerics
open Mibo
open Mibo.Diagnostics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// WorldView — the 3D world pass and the 2D HUD pass, reading ONLY
// the forced RenderFrame (the draw contract: no graph access at
// draw time; the sim clock rides the frame as frame.Time). The
// shell supplies the FrameDiag object; the VfxView owns its
// conversion buffers; the sub-presenters (map, towers, enemies,
// projectiles, hover overlays) own their scratch.
//
// Hover overlays:
//   * placement preview — the Models.selectionA ring at the hovered
//     cell, tinted by PlacementStatus (raylib instanced draws have
//     no per-instance colors, so a single .mesh draw with a tinted
//     Material3D.unlit override carries the tint).
//   * range disc — the tower's exact def.Range as a translucent blue
//     DISC (a thin unlit Cylinder, Opacity 0.30 — straight alpha: the
//     tint contributes color×opacity over the terrain).
// ─────────────────────────────────────────────────────────────

module WorldView =

  /// The placement ring model's tint per status.
  let inline statusColor(status: PlacementStatus) : Mibo.Color =
    match status with
    | PlacementStatus.Blocked -> Mibo.Color.Red
    | PlacementStatus.Affordable -> Mibo.Color.Green
    | PlacementStatus.TooExpensive -> Mibo.Color.rgb 255uy 210uy 0uy
    | PlacementStatus.Hidden -> Mibo.Color.White

  /// The hovered cell's world center (the placement preview ring).
  let inline hoverCenter(frame: RenderFrame) : Vector2 =
    match frame.HoverCell with
    | ValueNone -> Vector2.Zero
    | ValueSome struct (hx, hy) ->
      // 1 cell = 1 world unit, grid origin Zero.
      Vector2(float32 hx + 0.5f, float32 hy + 0.5f)

  /// The curated model set — warmed once on the first frame so no
  /// mid-frame load happens when a tower/enemy/overlay first appears
  /// (the map bake warms its own models in MapView). Mirrors the
  /// MonoDX12 warm set, keyed by NAME (the ModelMeshes key).
  let warmUsedModels() =
    let names = [|
      for m in Models.towerRoundParts do
        m.Name
      for m in Models.towerSquareParts do
        m.Name
      for m in Models.weapons do
        m.Name
      for m in Models.ammo do
        m.Name
      for m in Models.enemies do
        m.Name
      for m in Models.selectionRings do
        m.Name
    |]

    ModelMeshes.warm names

  /// Cached level-tag strings — one static allocation, reused every
  /// frame (no per-frame string building).
  let levelTags = [| "Lv 1"; "Lv 2"; "Lv 3"; "Lv 4"; "Lv 5"; "Lv 6" |]

  /// The screen-space offset of a tower's Lv tag from its projected
  /// body top (rough horizontal centering — Defli's fixed-offset
  /// idiom, no text measuring in the HUD pass).
  let tagOffset = Vector2(-20f, -26f)

/// The world pass presenter: owns the sub-presenters and the hover
/// overlays — constructed once in Program.fs, no module-level
/// mutable state.
[<Sealed>]
type WorldView(shell: Shell, vfx: VfxView) =

  let map = MapView()
  let towers = TowersView()
  let enemies = EnemiesView()
  let projectiles = ProjectilesView()

  let mutable warmed = false

  // The diagnostics overlay lines, formatted once per window (TotalFrames
  // moves only when a window closes; formatting every frame would
  // allocate on the hot path).
  let mutable diagLine1 = ""
  let mutable diagLine2 = ""
  let mutable diagLastWindow = 0L

  /// Placement preview: selection-a ring (1×0.05×1) on the hovered
  /// cell, tinted by the build status. One .mesh draw per sub-mesh
  /// with a tinted unlit material — the raylib instanced path has no
  /// per-instance colors, and this is a single overlay quad.
  let placementRing (frame: RenderFrame) (buffer: RenderBuffer3D) =
    match frame.PlacementPreview with
    | PlacementStatus.Hidden -> ()
    | status ->
      let info = Models.selectionA
      let meshes = ModelMeshes.resolve info
      let c = WorldView.hoverCenter frame
      let transform = Raymath.MatrixTranslate(c.X, 0.235f, c.Y)

      let material =
        Material3D.unlit(Mibo.Color.op_Implicit(WorldView.statusColor status))

      for mi = 0 to meshes.Length - 1 do
        let struct (mesh, _) = meshes[mi]
        buffer.mesh(mesh, transform, material) |> ignore

  /// The range marker rides ABOVE the terrain: the highest ground
  /// top under the range circle (raised tiles — hills/rocks — would
  /// otherwise clip through a flat ground-level disc). Samples the
  /// map's per-cell ground pieces (YOffset + model height).
  let rangeMarkerY
    (frame: RenderFrame)
    (hx: int)
    (hy: int)
    (radius: float32)
    : float32 =
    let terrain = MapModel.terrain frame.Map
    let center = Vector2(float32 hx + 0.5f, float32 hy + 0.5f)
    let mutable maxTop = TowerLayout.baseY
    let span = int(MathF.Ceiling radius)

    for y in max 0 (hy - span) .. min (terrain.Height - 1) (hy + span) do
      for x in max 0 (hx - span) .. min (terrain.Width - 1) (hx + span) do
        let c = Vector2(float32 x + 0.5f, float32 y + 0.5f)

        if Vector2.Distance(c, center) <= radius then
          let struct (ground, _) = MapModel.cellPieces frame.Map x y
          let top = ground.YOffset + ground.Model.SizeY

          if top > maxTop then
            maxTop <- top

    maxTop + 0.02f

  /// Range disc: the hovered own tower's effective range as a
  /// translucent tinted DISC (a thin Cylinder primitive) filling the
  /// range area. Requires BOTH a range def and a hover cell (the
  /// MonoDX12 rangeDisc guard — the two clients agree). Opacity<1
  /// routes it through the translucent pass (alpha blend, depth-write
  /// off) so it tints the area without blocking vision.
  let rangeRing (frame: RenderFrame) (buffer: RenderBuffer3D) =
    match frame.RangeRing, frame.HoverCell with
    | ValueSome def, ValueSome struct (hx, hy) ->
      let x = float32 hx + 0.5f
      let z = float32 hy + 0.5f
      let r = float32 def.Range

      // Unit cylinder centered on origin (Y [-0.5,+0.5]); scale to the
      // range radius + a thin height, lifted just above the tallest
      // ground the disc covers (terrain-aware — no floor clipping).
      let transform =
        Raymath.MatrixMultiply(
          Raymath.MatrixScale(r, 0.04f, r),
          Raymath.MatrixTranslate(x, rangeMarkerY frame hx hy r, z)
        )

      let material = {
        Material3D.unlit(Mibo.Color.op_Implicit(Mibo.Color.rgb 30uy 40uy 255uy)) with
            Opacity = 0.30f
      }

      buffer.mesh(Primitive3D.cylinder, transform, material) |> ignore
    | _ -> ()

  // ── The world pass ──────────────────────────────────────────

  /// The camera'd world pass (its own renderer — clears to the sky
  /// color). The neutral CameraState is converted at the edge
  /// (CameraView.toRaylib); everything world-space renders inside
  /// the camera block; the HUD renderer owns screen space.
  member _.Render
    (ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer3D)
    =
    // The per-frame context for lazy asset loads.
    ModelMeshes.setContext ctx

    if not warmed then
      warmed <- true
      WorldView.warmUsedModels()

    let camera = CameraView.toRaylib frame.Camera

    let sky = Raylib_cs.Color(108uy, 148uy, 190uy, 255uy)

    buffer
      .beginCameraWith(Camera3D.render camera |> Camera3D.withClear sky)
      .drop()

    // Lights: soft ambient + one shadow-casting directional (the
    // pipeline's shadow atlas follows the orbit target by default).
    buffer
      .setAmbientLight(
        {
          Color = Mibo.Color.rgb 205uy 215uy 230uy
          Intensity = 0.45f
        }
      )
      .addDirectionalLight(
        {
          Direction = Vector3.Normalize(Vector3(0.45f, -1f, 0.3f))
          Color = Mibo.Color.rgb 255uy 250uy 235uy
          Intensity = 1.05f
          CastsShadows = true
        }
      )
      .drop()

    map.View(ctx, frame, buffer)
    towers.View(ctx, frame, buffer)
    enemies.View(ctx, frame, buffer)
    projectiles.View(ctx, frame.Projectiles, buffer)
    vfx.View ctx frame.Vfx buffer

    placementRing frame buffer
    rangeRing frame buffer

    buffer.endCamera().drop()

  // ── The HUD pass ────────────────────────────────────────────

  /// Per-tower "Lv N" tags: each tower's body top (cell center, tile
  /// top + scaled stack height — TowerLayout.towerTop) projected
  /// world→screen through the sim camera pair, drawn in the HUD pass.
  /// Off-screen towers are skipped by the projection (behind the
  /// camera or outside the viewport → ValueNone).
  member private _.TowerLevelTags
    (ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer2D)
    =
    let font = Raylib.GetFontDefault()

    for KeyValueV(tid, s) in frame.TowerStatics do
      let level =
        frame.TowerLevels
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue 1

      let center = Cells.center s.Cell (Vector2.One)
      let top = Vector3(center.X, TowerLayout.towerTop s.Def, center.Y)

      match
        Camera.worldToScreen
          (float32 ctx.WindowWidth)
          (float32 ctx.WindowHeight)
          top
          frame.Camera
      with
      | ValueNone -> ()
      | ValueSome screen ->
        buffer
          .text(
            font,
            WorldView.levelTags[min (max level 1) 6 - 1],
            screen + WorldView.tagOffset,
            14f,
            layer = Layers.Hud
          )
          .drop()

  /// Screen-space HUD pass (own noClear renderer): reads the frame
  /// only — same text/anchors as Defli's hudView.
  member this.Hud
    (ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer2D)
    =
    let font = Raylib.GetFontDefault()

    buffer
      .text(
        font,
        $"Gold: %d{frame.Gold}   Lives: %d{frame.Lives}   %s{frame.Banner}   Tower: %s{frame.SelectedTower.Name} (0-9)",
        Vector2(12f, 10f),
        22f,
        layer = Layers.Hud
      )
      .drop()

    buffer
      .text(
        font,
        "WASD/arrows or middle-drag: pan   wheel: zoom   Home: reset   right-click: upgrade",
        Vector2(12f, float32 ctx.WindowHeight - 30f),
        16f,
        layer = Layers.Hud
      )
      .drop()

    this.TowerLevelTags(ctx, frame, buffer)

    if frame.GameOver then
      let text = "GAME OVER — press R to restart"
      let size = Raylib.MeasureTextEx(font, text, 40f, 1f)

      buffer
        .text(
          font,
          text,
          Vector2(
            (float32 ctx.WindowWidth - size.X) / 2f,
            (float32 ctx.WindowHeight - size.Y) / 2f
          ),
          40f,
          layer = Layers.Hud
        )
        .drop()

    if shell.ShowDiag then
      match Diagnostics.tryGetProfiler ctx with
      | ValueSome profiler ->
        let stats = profiler.Snapshot

        if stats.TotalFrames <> diagLastWindow then
          diagLastWindow <- stats.TotalFrames
          // Two lines: raylib text does not render newlines.
          let lines = (Diagnostics.format stats).Split('\n')
          diagLine1 <- lines[0]
          diagLine2 <- lines[1]

        let yellow = Mibo.Color.rgb 255uy 210uy 0uy

        buffer.text(
          font,
          diagLine1,
          Vector2(12f, 40f),
          18f,
          tint = yellow,
          layer = Layers.Hud
        )
        |> ignore

        buffer.text(
          font,
          diagLine2,
          Vector2(12f, 64f),
          18f,
          tint = yellow,
          layer = Layers.Hud
        )
        |> ignore
      | ValueNone -> ()
