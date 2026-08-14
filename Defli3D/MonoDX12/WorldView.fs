namespace Defli3D.MonoGame

open System
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// WorldView — the 3D world pass and the 2D HUD pass, reading ONLY
// the forced RenderFrame (the draw contract: no graph access at
// draw time). The shell supplies the FrameDiag object; the VfxView
// owns its conversion buffers; the sim clock (Time) comes from the
// observer (Program.fs) for draw-side animation.
// ─────────────────────────────────────────────────────────────

module WorldView =

  /// XNA Matrix for the overlay transforms (XNA Vector/Color stay out
  /// of scope — System.Numerics + Mibo are the opened defaults).
  type private Matrix = Microsoft.Xna.Framework.Matrix

  // ── Frame-level presentation state ──────────────────────────

  /// Sky clear color for the world camera block.
  let private sky = Microsoft.Xna.Framework.Color(46, 58, 72)

  let private ambient: AmbientLight3D = {
    Color = Color.White
    Intensity = 0.45f
  }

  let private sun: DirectionalLight3D = {
    Direction = Vector3(0.5f, -0.8f, 0.3f)
    Color = Color.White
    Intensity = 1f
    CastsShadows = true
  }

  /// The hover overlays share the world pass's InstanceScratch (reset
  /// at the top of worldView; the preview is added last and drawn on
  /// top via the final InstanceScratch.draw).
  /// Warm the curated model set once on the first frame so no
  /// mid-frame Content.Load happens when a tower/enemy/overlay
  /// first appears (the map bake warms its own models in MapView).
  let mutable private warmed = false

  let private warmUsedModels() =
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
  let inline private previewTint
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
  let private rangeDiscColor = Microsoft.Xna.Framework.Color(30, 40, 255)

  /// Unit primitives — the range disc is a thin Cylinder. Built once.
  let mutable private primitives: Primitive3D.PrimitiveSet voption = ValueNone

  /// The hover overlays: the placement preview disc (selection-a at
  /// the hover cell, tinted by build status) and the range ring of
  /// the hovered own tower (selection-b's octagon scaled so its outer
  /// vertices land exactly on the fire range — everything under the
  /// marker is in range; a flat 0.25 Y-scale keeps the band 0.05
  /// tall so it doesn't block vision). Both go through the shared
  /// InstanceScratch (reset → fill → final draw on top — each view
  /// owns its reset, so the last draw emits only the overlays).
  /// NOTE: no line3D circle here — line primitives are broken on the
  /// MonoGame DX12 runtime (the PSO topology type is never set, line
  /// draws rasterize as garbage). The ring mesh works everywhere.
  let private hoverOverlays (frame: RenderFrame) (buffer: RenderBuffer3D) =
    InstanceScratch.reset()

    match frame.PlacementPreview with
    | PlacementStatus.Hidden -> ()
    | status ->
      frame.HoverCell
      |> ValueOption.iter(fun struct (hx, hy) ->
        let x = float32 hx + 0.5f
        let z = float32 hy + 0.5f

        InstanceScratch.addTinted
          Models.selectionA.Path
          (Matrix.CreateTranslation(x, 0.21f, z))
          (previewTint status))

    ()

  /// Builds the unit primitives (range disc) once — the Cylinder needs a
  /// GraphicsDevice, so it is lazy on the first frame.
  let private ensurePrimitives(ctx: GameContext) =
    match primitives with
    | ValueSome _ -> ()
    | ValueNone ->
      primitives <-
        ValueSome(Primitive3D.create(MonoGameGameContext.getGraphicsDevice ctx))

  /// The hovered tower's firing range as a translucent tinted DISC (a thin
  /// Cylinder) filling the range area — replaces the old flat selection-b
  /// octagon ring. Opacity<1 routes it through the translucent pass (alpha
  /// blend, depth-write off) so it tints the area without blocking vision.
  let private rangeDisc (frame: RenderFrame) (buffer: RenderBuffer3D) =
    match primitives, frame.RangeRing, frame.HoverCell with
    | ValueSome set, ValueSome def, ValueSome struct (hx, hy) ->
      let x = float32 hx + 0.5f
      let z = float32 hy + 0.5f
      let r = float32 def.Range
      // Unit cylinder is centered on origin (Y [-0.5,+0.5]); scale to the
      // range radius + a thin height, lift just above the tile top (0.2).
      let transform =
        Matrix.CreateScale(r, 0.04f, r) * Matrix.CreateTranslation(x, 0.22f, z)

      let material = {
        Material3D.unlit(rangeDiscColor) with
            Opacity = 0.30f
      }

      buffer.mesh(set.Cylinder, transform, material) |> ignore
    | _ -> ()

  // ── The world pass ──────────────────────────────────────────

  /// The camera'd world pass (its own renderer — clears to sky).
  let worldView
    (shell: Shell)
    (vfx: VfxView)
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer3D)
    =
    Diagnostics.drawn (Diagnostics.tickStart()) shell.Diag

    ModelCache.setContext ctx

    if not warmed then
      warmed <- true
      warmUsedModels()

    // Camera block: the edge builds the native camera from the
    // frame's neutral snapshot; everything world-space renders
    // inside; the HUD renderer (separate noClear pass) owns screen
    // space.
    let camera =
      CameraView.toMono
        (float32 ctx.WindowWidth)
        (float32 ctx.WindowHeight)
        frame.Camera

    buffer
      .beginCameraWith(Camera3D.render camera |> Camera3D.withClear sky)
      .setAmbientLight(ambient)
      .addDirectionalLight(sun)
      .drop()

    MapView.view ctx frame buffer
    TowersView.view ctx frame buffer
    EnemiesView.view ctx frame buffer
    ProjectilesView.view ctx frame.Projectiles buffer
    vfx.View ctx frame.Vfx buffer
    hoverOverlays frame buffer
    InstanceScratch.draw buffer
    ensurePrimitives ctx
    rangeDisc frame buffer
    buffer.endCamera().drop()

  // ── The HUD pass ────────────────────────────────────────────

  /// Cached level-tag strings — one static allocation, reused every
  /// frame (no per-frame string building).
  let private levelTags = [| "Lv 1"; "Lv 2"; "Lv 3"; "Lv 4"; "Lv 5"; "Lv 6" |]

  /// The screen-space offset of a tower's Lv tag from its projected
  /// body top (rough horizontal centering — Defli's fixed-offset
  /// idiom, no text measuring in the HUD pass).
  let private tagOffset = Vector2(-20f, -26f)

  /// Per-tower "Lv N" tags: each tower's body top (cell center, tile
  /// top + scaled stack height — TowerLayout.towerTop) projected
  /// world→screen through the sim camera pair, drawn in the HUD pass.
  /// Off-screen towers are skipped by the projection (behind the
  /// camera or outside the viewport → ValueNone).
  let private towerLevelTags
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let font = assets.Font Paths.Font

    for KeyValueV(tid, s) in frame.TowerStatics do
      let level =
        frame.TowerLevels
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue 1

      let center = Cells.center s.Cell (Vector2.One)
      let top = Vector3(center.X, TowerLayout.towerTop s.Def level, center.Y)

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
            levelTags[min (max level 1) 6 - 1],
            screen + tagOffset,
            0.75f,
            layer = Layers.Hud
          )
          .drop()

  /// Screen-space HUD pass (own renderer, noClear): reads the frame
  /// only. Font scales match Defli's MonoGame client.
  let hudView
    (shell: Shell)
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let font = assets.Font Paths.Font

    buffer
      .text(
        font,
        $"Gold: %d{frame.Gold}   Lives: %d{frame.Lives}   %s{frame.Banner}   Tower: %s{frame.SelectedTower.Name} (1/2/3)",
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

    towerLevelTags ctx frame buffer

    if frame.GameOver then
      buffer
        .text(
          font,
          "GAME OVER - press R to restart",
          Vector2(430f, 360f),
          2f,
          layer = Layers.Hud
        )
        .drop()

    if shell.Diag.Visible then
      buffer.frameDiagnostics(font, shell.Diag, Vector2(12f, 40f)).drop()
      buffer.worldDiagnostics(font, frame.Diag, Vector2(12f, 64f)).drop()
