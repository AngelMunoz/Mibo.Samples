namespace Defli3D.Raylib

open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// WorldView — the 3D world pass and the 2D HUD pass, reading ONLY
// the forced RenderFrame (the draw contract: no graph access at
// draw time). The world pass opens the camera block, registers the
// lights (1 ambient + 1 shadow-casting directional), and draws
// map/towers/enemies/projectiles/vfx plus the hover overlays; the
// HUD pass (its own noClear Renderer2D, registered after the 3D
// renderer in Program.fs) owns screen space.
//
// Hover overlays:
//   * placement preview — the Models.selectionA ring at the hovered
//     cell, tinted by PlacementStatus (raylib instanced draws have
//     no per-instance colors, so a single .mesh draw with a tinted
//     Material3D.unlit override carries the tint).
//   * range ring — the Models.selectionB octagon scaled to the
//     tower's exact def.Range, flat on the tile tops and translucent
//     blue (unlit, Opacity 0.43 — the MonoGame selection tint; no
//     more line circle).
// ─────────────────────────────────────────────────────────────

module WorldView =

  /// The placement ring model + its tint per status.
  let inline private statusColor(status: PlacementStatus) : Mibo.Color =
    match status with
    | PlacementStatus.Blocked -> Mibo.Color.Red
    | PlacementStatus.Affordable -> Mibo.Color.Green
    | PlacementStatus.TooExpensive -> Mibo.Color.rgb 255uy 210uy 0uy
    | PlacementStatus.Hidden -> Mibo.Color.White

  /// The hovered cell's world center (the placement preview ring).
  let inline private hoverCenter(frame: RenderFrame) : Vector2 =
    match frame.HoverCell with
    | ValueNone -> Vector2.Zero
    | ValueSome struct (hx, hy) ->
      // 1 cell = 1 world unit, grid origin Zero.
      Vector2(float32 hx + 0.5f, float32 hy + 0.5f)

  /// Placement preview: selection-a ring (1×0.05×1) on the hovered
  /// cell, tinted by the build status. One .mesh draw per sub-mesh
  /// with a tinted unlit material — the raylib instanced path has no
  /// per-instance colors, and this is a single overlay quad.
  let private placementRing
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer3D)
    =
    match frame.PlacementPreview with
    | PlacementStatus.Hidden -> ()
    | status ->
      let info = Models.selectionA
      let meshes = ModelMeshes.resolve info
      let c = hoverCenter frame
      let transform = Raymath.MatrixTranslate(c.X, 0.235f, c.Y)

      let material =
        Material3D.unlit(Mibo.Color.op_Implicit(statusColor status))

      for mi = 0 to meshes.Length - 1 do
        let struct (mesh, _) = meshes[mi]
        buffer.mesh(mesh, transform, material) |> ignore

  /// selection-b's outer-vertex radius — MEASURED via vertex probe:
  /// the ring is an octagon whose outer vertices sit at
  /// √(0.5² + 0.4²) = 0.6403 (the 1.0 AABB's corners are NOT on the
  /// mesh), so scaling it to the AABB overdraws the radius by 1.28×.
  /// The range ring divides by this so the ring lands exactly on the
  /// Range disc: the hovered own tower's effective range as a translucent
  /// tinted DISC (a thin Cylinder primitive) filling the range area —
  /// replaces the old flat selection-b octagon ring. Opacity<1 routes it
  /// through the translucent pass (alpha blend, depth-write off) so it tints
  /// the area without blocking vision.
  let private rangeRing
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer3D)
    =
    frame.RangeRing
    |> ValueOption.iter(fun def ->
      let c = hoverCenter frame
      let r = float32 def.Range

      // Unit cylinder centered on origin (Y [-0.5,+0.5]); scale to the range
      // radius + a thin height, lift just above the tile top (0.2).
      let transform =
        Raymath.MatrixMultiply(
          Raymath.MatrixScale(r, 0.04f, r),
          Raymath.MatrixTranslate(c.X, 0.22f, c.Y)
        )

      let material = {
        Material3D.unlit(
          Mibo.Color.op_Implicit(Mibo.Color.rgb 30uy 40uy 255uy)
        ) with
            Opacity = 0.30f
      }

      buffer.mesh(Primitive3D.cylinder, transform, material) |> ignore)

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
    let font = Raylib.GetFontDefault()

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
            14f,
            layer = Layers.Hud
          )
          .drop()

  /// The camera'd world pass (its own renderer — clears to the sky
  /// color). The neutral CameraState is converted at the edge
  /// (CameraView.toRaylib); everything world-space renders inside the
  /// camera block; the HUD renderer owns screen space.
  let worldView
    (shell: Shell)
    (vfx: VfxView)
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer3D)
    =
    Diagnostics.drawn (Diagnostics.tickStart()) shell.Diag

    // The per-frame context for lazy asset loads (the instanced map
    // context's resolver takes none — Platformer3D's recipe).
    ModelMeshes.setContext ctx

    let camera =
      CameraView.toRaylib
        (float32 ctx.WindowWidth, float32 ctx.WindowHeight)
        frame.Camera

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

    MapView.view ctx frame buffer
    TowersView.view ctx frame.TowerStatics frame.TowerLevels frame.Alive buffer
    EnemiesView.view ctx frame.Alive frame.Defs buffer
    ProjectilesView.view ctx frame.Projectiles buffer
    vfx.View ctx frame.Vfx buffer

    placementRing ctx frame buffer
    rangeRing ctx frame buffer

    buffer.endCamera().drop()

  /// Screen-space HUD pass (own noClear renderer): reads the frame
  /// only — same text/anchors as Defli's hudView.
  let hudView
    (shell: Shell)
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer2D)
    =
    let font = Raylib.GetFontDefault()

    buffer
      .text(
        font,
        $"Gold: %d{frame.Gold}   Lives: %d{frame.Lives}   %s{frame.Banner}   Tower: %s{frame.SelectedTower.Name} (1/2/3)",
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

    towerLevelTags ctx frame buffer

    if frame.GameOver then
      buffer
        .text(
          font,
          "GAME OVER — press R to restart",
          Vector2(430f, 360f),
          40f,
          layer = Layers.Hud
        )
        .drop()

    if shell.Diag.Visible then
      buffer.frameDiagnostics(font, shell.Diag, Vector2(12f, 40f)).drop()
      buffer.worldDiagnostics(font, frame.Diag, Vector2(12f, 64f)).drop()
