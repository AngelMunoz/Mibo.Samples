namespace PrimitiveGallery.MonoGame

open System
open System.Numerics
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open PrimitiveGallery

// ─────────────────────────────────────────────────────────────
// Screen3DView — the 3D shapes pass (Shapes3D full-screen, Split
// right half) plus the screen-space HUD. Reads Layout3D.shapes /
// Layout3D.lines / Layout3D.labels. Fixed camera, two lights, the
// shared unit-primitive cache (Prims), and the fluent Draw DSL.
// ─────────────────────────────────────────────────────────────

module Screen3DView =

  /// XNA matrix abbreviation — the DSL mesh witness takes an XNA Matrix
  /// transform (mirrors Defli3D/MonoDX12/WorldView.fs).
  type private XnaMatrix = Microsoft.Xna.Framework.Matrix

  /// Camera + material clear color (the ClearColor is an XNA Color).
  let private clearColor = Microsoft.Xna.Framework.Color(40, 44, 52)

  let private ambientLight: AmbientLight3D = {
    Color = Mibo.Color.rgb 210uy 215uy 225uy
    Intensity = 0.55f
  }

  let private sunLight: DirectionalLight3D = {
    Direction = Vector3.Normalize(Vector3(0.5f, -1f, 0.4f))
    Color = Mibo.Color.rgb 255uy 250uy 235uy
    Intensity = 1.1f
    CastsShadows = false
  }

  /// Fixed camera (Camera3D takes XNA Vector3 position/target).
  let private camera =
    Camera3D.create
      (Microsoft.Xna.Framework.Vector3(0f, 6f, 14f))
      (Microsoft.Xna.Framework.Vector3(0f, 1f, 0f))
      (45f * MathF.PI / 180f)

  /// Inspection spin speed (radians/second) — one full turn ≈ 8s (matches
  /// the raylib client).
  let private rotationSpeed = 0.8f

  /// Lit albedo material — shading reveals vertex/normal orientation.
  let private material(color: Mibo.Color) : Material3D =
    Material3D.colored(MonoGameColor.toMonoGameColor color)

  /// The MonoGame plane primitive lies on XY with its normal on +Z; raylib's
  /// GenMeshPlane lies on XZ with normal +Y. Lay planes flat so this client
  /// matches the raylib picture. (The orientation difference is a known
  /// framework divergence — FPSSample orients decals against the +Z plane —
  /// so the gallery compensates per-backend like FPSSample does.)
  let private layPlaneFlat = XnaMatrix.CreateRotationX(-MathF.PI / 2f)

  /// Unit primitives are centered on the origin: spin around Y through the
  /// shape's own center, then scale, then translate. The ground stays static.
  /// Planes scale in their LOCAL axes first (raylib semantics: scale.X/Z are
  /// the world extents of a flat plane), then lay flat, then spin.
  let private transform
    (position: Vector3)
    (scale: Vector3)
    (spins: bool)
    (layFlat: bool)
    (elapsed: float32)
    : XnaMatrix =
    let spin =
      if spins then
        XnaMatrix.CreateRotationY(elapsed * rotationSpeed)
      else
        XnaMatrix.Identity

    let basis = if layFlat then layPlaneFlat * spin else spin

    let s =
      if layFlat then
        // local X → world X (scale.X), local Y → world Z (scale.Z)
        XnaMatrix.CreateScale(scale.X, scale.Z, scale.Y)
      else
        XnaMatrix.CreateScale(scale.X, scale.Y, scale.Z)

    s * basis * XnaMatrix.CreateTranslation(position.X, position.Y, position.Z)

  let private drawShape
    (buffer: RenderBuffer3D)
    (prims: Primitive3D.PrimitiveSet)
    (elapsed: float32)
    (shape: Shape3D)
    : unit =
    let spins, layFlat =
      match shape with
      | Shape3D.Plane(name, _, _, _) -> (name <> "ground"), true
      | _ -> true, false

    match shape with
    | Shape3D.Cube(_, p, s, c) ->
      buffer.mesh(prims.Cube, transform p s spins layFlat elapsed, material c)
      |> ignore
    | Shape3D.Sphere(_, p, s, c) ->
      buffer.mesh(prims.Sphere, transform p s spins layFlat elapsed, material c)
      |> ignore
    | Shape3D.Cylinder(_, p, s, c) ->
      buffer.mesh(
        prims.Cylinder,
        transform p s spins layFlat elapsed,
        material c
      )
      |> ignore
    | Shape3D.Plane(_, p, s, c) ->
      buffer.mesh(prims.Plane, transform p s spins layFlat elapsed, material c)
      |> ignore
    | Shape3D.Torus(_, p, s, c) ->
      buffer.mesh(prims.Torus, transform p s spins layFlat elapsed, material c)
      |> ignore
    | Shape3D.Cone(_, p, s, c) ->
      buffer.mesh(prims.Cone, transform p s spins layFlat elapsed, material c)
      |> ignore

  /// The 3D pass: full-screen for Shapes3D, split-right for Split, and a
  /// full-screen sky clear (no geometry) for Shapes2D so the noClear 2D
  /// pass always draws on a clean frame.
  let draw3D
    (ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer3D)
    : unit =
    match frame.Screen with
    | Screen.Shapes2D ->
      // No 3D geometry on the 2D screen, but the frame must still be
      // cleared before the noClear 2D pass draws: begin a full-screen
      // camera with the sky clear and draw nothing.
      buffer
        .beginCameraWith(
          Camera3D.render camera |> Camera3D.withClear clearColor
        )
        .endCamera()
        .drop()
    | Screen.Shapes3D
    | Screen.Split ->
      let gd = MonoGameGameContext.getGraphicsDevice ctx
      let prims = Prims.get gd

      let config =
        match frame.Screen with
        | Screen.Split ->
          Camera3D.splitScreenRight
            camera
            clearColor
            (Microsoft.Xna.Framework.Rectangle(0, 0, 1280, 720))
        | _ -> Camera3D.render camera |> Camera3D.withClear clearColor

      buffer
        .beginCameraWith(config)
        .setAmbientLight(ambientLight)
        .addDirectionalLight(sunLight)
        .drop()

      for shape in Layout3D.shapes do
        drawShape buffer prims frame.Elapsed shape

      for line in Layout3D.lines do
        buffer.line3D(line.Start, line.Finish, line.Color) |> ignore

      buffer.endCamera() |> ignore

  /// The HUD for the Shapes3D screen: the screen-space label anchors
  /// from Layout3D.labels, then the title and help line.
  let hud3D
    (ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    : unit =
    let assets = GameContext.getService<IAssets> ctx
    let font = assets.Font Paths.Font

    buffer.setSamplerState(SamplerState.PointClamp, layer = 0<RenderLayer>)
    |> ignore

    for name, anchor in Layout3D.labels do
      buffer.text(font, name, anchor, 1.0f, layer = Layers.Labels) |> ignore

    buffer.text(
      font,
      Hud.title frame.Screen,
      Vector2(12f, 4f),
      1.0f,
      layer = Layers.Labels
    )
    |> ignore

    buffer.text(font, Hud.help, Vector2(850f, 8f), 1.0f, layer = Layers.Labels)
    |> ignore

  /// The HUD for the Shapes2D + Split screens: title + help only (the
  /// cells themselves are drawn by Screen2DView.draw2D).
  let hud2D
    (ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    : unit =
    let assets = GameContext.getService<IAssets> ctx
    let font = assets.Font Paths.Font

    buffer.setSamplerState(SamplerState.PointClamp, layer = 0<RenderLayer>)
    |> ignore

    buffer.text(
      font,
      Hud.title frame.Screen,
      Vector2(12f, 4f),
      1.0f,
      layer = Layers.Labels
    )
    |> ignore

    buffer.text(font, Hud.help, Vector2(850f, 8f), 1.0f, layer = Layers.Labels)
    |> ignore
