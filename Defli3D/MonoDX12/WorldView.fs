namespace Defli3D.MonoGame

open System
open System.Numerics
open Mibo
open Mibo.Diagnostics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
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
// ─────────────────────────────────────────────────────────────

module WorldView =

  // ── Frame-level presentation state ──────────────────────────

  /// Sky clear color for the world camera block.
  let sky = Microsoft.Xna.Framework.Color(46, 58, 72)

  let ambient: AmbientLight3D = {
    Color = Color.White
    Intensity = 0.45f
  }

  let sun: DirectionalLight3D = {
    Direction = Vector3(0.5f, -0.8f, 0.3f)
    Color = Color.White
    Intensity = 1f
    CastsShadows = true
  }

  /// The curated model set — warmed once on the first frame so no
  /// mid-frame Content.Load happens when a tower/enemy/overlay first
  /// appears (the map bake warms its own models in MapView).
  let warmUsedModels() =
    let names = [|
      for m in Models.towerRoundParts do
        m.Path
      for m in Models.towerSquareParts do
        m.Path
      for m in Models.weapons do
        m.Path
      for m in Models.ammo do
        m.Path
      for m in Models.enemies do
        m.Path
      for m in Models.selectionRings do
        m.Path
    |]

    ModelCache.warm names

  // ── Hover overlays ──────────────────────────────────────────

  /// Placement preview tint by build status (per-instance colors —
  /// albedo × rgb, alpha × a; the translucent alpha routes the draw
  /// through the pipeline's sorted translucent pass).
  let inline previewTint
    (status: PlacementStatus)
    : Microsoft.Xna.Framework.Color =
    match status with
    | PlacementStatus.Hidden -> Microsoft.Xna.Framework.Color.White
    | PlacementStatus.Blocked -> Microsoft.Xna.Framework.Color(230, 50, 50, 150)
    | PlacementStatus.Affordable ->
      Microsoft.Xna.Framework.Color(60, 200, 70, 150)
    | PlacementStatus.TooExpensive ->
      Microsoft.Xna.Framework.Color(255, 210, 0, 150)

  /// selection-b's outer vertex radius (measured via vertex probe:
  /// the octagon's corners sit at (±0.5, ±0.4) → √(0.5² + 0.4²) ≈
  /// 0.6403). The range ring divides the tower's fire range by this
  /// so the octagon's corners land exactly on the range circle —
  /// everything under the marker is in range.
  /// Range disc tint — translucent pure blue. Opacity<1 (set on the material)
  /// routes the draw through the pipeline's sorted translucent pass so the disc
  /// tints the firing-range area without blocking vision.
  let rangeDiscColor = Microsoft.Xna.Framework.Color(30, 40, 255)

  /// Cached level-tag strings — one static allocation, reused every
  /// frame (no per-frame string building).
  let levelTags = [| "Lv 1"; "Lv 2"; "Lv 3"; "Lv 4"; "Lv 5"; "Lv 6" |]

  /// The screen-space offset of a tower's Lv tag from its projected
  /// body top (rough horizontal centering — Defli's fixed-offset
  /// idiom, no text measuring in the HUD pass).
  let tagOffset = Vector2(-20f, -26f)

/// The world pass presenter: owns the sub-presenters, the hover
/// overlay groups and the unit primitives — constructed once in
/// Program.fs, no module-level mutable state.
[<Sealed>]
type WorldView(shell: Shell, vfx: VfxView) =

  let map = MapView()
  let towers = TowersView()
  let enemies = EnemiesView()
  let projectiles = ProjectilesView()

  /// The hover overlays' own groups (the preview is added last and
  /// drawn on top via the final Draw).
  let overlays = InstanceGroups()

  let mutable warmed = false

  // The diagnostics overlay lines, formatted once per window (TotalFrames
  // moves only when a window closes; formatting every frame would
  // allocate on the hot path).
  let mutable diagLine1 = ""
  let mutable diagLine2 = ""
  let mutable diagLastWindow = 0L

  /// Unit primitives — the range disc is a thin Cylinder. Built once.
  let mutable primitives: Primitive3D.PrimitiveSet voption = ValueNone

  /// Builds the unit primitives (range disc) once — the Cylinder needs a
  /// GraphicsDevice, so it is lazy on the first frame.
  let ensurePrimitives(ctx: GameContext) =
    match primitives with
    | ValueSome _ -> ()
    | ValueNone ->
      primitives <-
        ValueSome(Primitive3D.create(MonoGameGameContext.getGraphicsDevice ctx))

  /// Stages the hover overlays into the overlay groups: the placement
  /// preview disc (selection-a at the hover cell, tinted by build
  /// status) — the final Draw emits only the overlays, on top.
  /// NOTE: no line3D circle here — line primitives are broken on the
  /// MonoGame DX12 runtime (the PSO topology type is never set, line
  /// draws rasterize as garbage). The ring mesh works everywhere.
  let stageHoverOverlays(frame: RenderFrame) =
    overlays.Clear()

    match frame.PlacementPreview with
    | PlacementStatus.Hidden -> ()
    | status ->
      frame.HoverCell
      |> ValueOption.iter(fun struct (hx, hy) ->
        let x = float32 hx + 0.5f
        let z = float32 hy + 0.5f

        overlays.AddTinted(
          Models.selectionA.Path,
          Microsoft.Xna.Framework.Matrix.CreateTranslation(x, 0.21f, z),
          WorldView.previewTint status
        ))

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
    let center = System.Numerics.Vector2(float32 hx + 0.5f, float32 hy + 0.5f)

    let mutable maxTop = TowerLayout.baseY
    let span = int(MathF.Ceiling radius)

    for y in max 0 (hy - span) .. min (terrain.Height - 1) (hy + span) do
      for x in max 0 (hx - span) .. min (terrain.Width - 1) (hx + span) do
        let c = System.Numerics.Vector2(float32 x + 0.5f, float32 y + 0.5f)

        if System.Numerics.Vector2.Distance(c, center) <= radius then
          let struct (ground, _) = MapModel.cellPieces frame.Map x y
          let top = ground.YOffset + ground.Model.SizeY

          if top > maxTop then
            maxTop <- top

    maxTop + 0.02f

  /// The hovered tower's firing range as a translucent tinted DISC (a thin
  /// Cylinder) filling the range area — replaces the old flat selection-b
  /// octagon ring. Opacity<1 routes it through the translucent pass (alpha
  /// blend, depth-write off) so it tints the area without blocking vision.
  /// Lifted just above the tallest ground it covers (terrain-aware — no
  /// floor clipping).
  let rangeDisc (frame: RenderFrame) (buffer: RenderBuffer3D) =
    match primitives, frame.RangeRing, frame.HoverCell with
    | ValueSome set, ValueSome def, ValueSome struct (hx, hy) ->
      let x = float32 hx + 0.5f
      let z = float32 hy + 0.5f
      let r = float32 def.Range
      // Unit cylinder is centered on origin (Y [-0.5,+0.5]); scale to the
      // range radius + a thin height.
      let transform =
        Microsoft.Xna.Framework.Matrix.CreateScale(r, 0.04f, r)
        * Microsoft.Xna.Framework.Matrix.CreateTranslation(
          x,
          rangeMarkerY frame hx hy r,
          z
        )

      let material = {
        Material3D.unlit(WorldView.rangeDiscColor) with
            Opacity = 0.30f
      }

      buffer.meshSlice(set.Cylinder, transform, material).drop()
    | _ -> ()

  // ── The world pass ──────────────────────────────────────────

  /// The camera'd world pass (its own renderer — clears to sky).
  member _.Render
    (ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer3D)
    =
    ModelCache.setContext ctx

    if not warmed then
      warmed <- true
      WorldView.warmUsedModels()

    // Camera block: the edge builds the native camera from the
    // frame's neutral snapshot; everything world-space renders
    // inside; the HUD renderer (separate noClear pass) owns screen
    // space.
    let camera = CameraView.toMono frame.Camera

    buffer
      .beginCameraWith(
        Camera3D.render camera |> Camera3D.withClear WorldView.sky
      )
      .setAmbientLight(WorldView.ambient)
      .addDirectionalLight(WorldView.sun)
      .drop()

    map.View(ctx, frame, buffer)
    towers.View(ctx, frame, buffer)
    enemies.View(ctx, frame, buffer)
    projectiles.View(ctx, frame.Projectiles, buffer)
    vfx.View ctx frame.Vfx buffer
    stageHoverOverlays frame
    overlays.Draw buffer
    ensurePrimitives ctx
    rangeDisc frame buffer
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
    let assets = GameContext.getService<IAssets> ctx
    let font = assets.Font Paths.Font

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
            0.75f,
            layer = Layers.Hud
          )
          .drop()

  /// Screen-space HUD pass (own renderer, noClear): reads the frame
  /// only. Font scales match Defli's MonoGame client.
  member this.Hud
    (ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let font = assets.Font Paths.Font

    buffer
      .text(
        font,
        $"Gold: %d{frame.Gold}   Lives: %d{frame.Lives}   %s{frame.Banner}   Tower: %s{frame.SelectedTower.Name} (1-0)",
        Vector2(12f, 10f),
        1.5f,
        layer = Layers.Hud
      )
      .drop()

    buffer
      .text(
        font,
        "WASD/arrows or middle-drag: pan   wheel: zoom   Home: reset   right-click: upgrade",
        Vector2(12f, float32 ctx.WindowHeight - 30f),
        1.25f,
        layer = Layers.Hud
      )
      .drop()

    this.TowerLevelTags(ctx, frame, buffer)

    if frame.GameOver then
      let text = "GAME OVER - press R to restart"
      let size = font.MeasureString(text) * 2f

      buffer
        .text(
          font,
          text,
          Vector2(
            (float32 ctx.WindowWidth - size.X) / 2f,
            (float32 ctx.WindowHeight - size.Y) / 2f
          ),
          2f,
          layer = Layers.Hud
        )
        .drop()

    if shell.ShowDiag then
      match Diagnostics.tryGetProfiler ctx with
      | ValueSome profiler ->
        let stats = profiler.Snapshot

        if stats.TotalFrames <> diagLastWindow then
          diagLastWindow <- stats.TotalFrames

          // Two lines: the draw side stays backend neutral.
          let lines = (Diagnostics.format stats).Split('\n')
          diagLine1 <- lines[0]
          diagLine2 <- lines[1]

        let yellow = Mibo.Color.rgb 255uy 210uy 0uy

        // The text size argument is a scale on MonoGame (the SpriteFont
        // bakes its pixel size), not a pixel size like on raylib.
        buffer.text(
          font,
          diagLine1,
          Vector2(12f, 40f),
          1f,
          tint = yellow,
          layer = Layers.Hud
        )
        |> ignore

        buffer.text(
          font,
          diagLine2,
          Vector2(12f, 64f),
          1f,
          tint = yellow,
          layer = Layers.Hud
        )
        |> ignore
      | ValueNone -> ()
