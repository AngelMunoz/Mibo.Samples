namespace PrimitiveGallery.Raylib

open System
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Raylib_cs
open PrimitiveGallery

// ─────────────────────────────────────────────────────────────
// Screen3DView — the 3D shapes pass (Shapes3D full-screen and the
// right half of Split) plus the HUD dispatch. Reads ONLY the forced
// RenderFrame.
//
// A fixed camera looks at the primitives row; lights are ambient +
// one directional (illumination only — lighting is not under test).
// Labels are drawn screen-space from Layout3D.labels (static anchors).
// ─────────────────────────────────────────────────────────────

module Screen3DView =

  /// The fixed camera for every 3D screen. raylib's Camera3D.FovY is in
  /// DEGREES (see Defli3D/Raylib/CameraView.fs and RaylibHelpers), so 45
  /// means a 45° vertical field of view.
  let private camera: Raylib_cs.Camera3D =
    Camera3D(
      Vector3(0f, 6f, 14f),
      Vector3(0f, 1f, 0f),
      Vector3.UnitY,
      45f,
      CameraProjection.Perspective
    )

  let private skyColor = Raylib_cs.Color(40uy, 44uy, 52uy, 255uy)

  /// Inspection spin speed (radians/second) — one full turn ≈ 8s.
  let private rotationSpeed = 0.8f

  /// Draws a single Shape3D primitive: spin around Y through the shape's own
  /// center (the ground stays static), scale, then translate. Lit albedo
  /// material so shading reveals vertex/normal orientation.
  let private drawShape
    (elapsed: float32)
    (buffer: RenderBuffer3D)
    (s: Shape3D)
    : unit =
    let mesh, position, scale, color, spins =
      match s with
      | Shape3D.Cube(_, p, sc, co) -> Primitive3D.cube, p, sc, co, true
      | Shape3D.Sphere(_, p, sc, co) -> Primitive3D.sphere, p, sc, co, true
      | Shape3D.Cylinder(_, p, sc, co) -> Primitive3D.cylinder, p, sc, co, true
      | Shape3D.Plane(name, p, sc, co) ->
        Primitive3D.plane, p, sc, co, (name <> "ground")
      | Shape3D.Torus(_, p, sc, co) -> Primitive3D.torus, p, sc, co, true
      | Shape3D.Cone(_, p, sc, co) -> Primitive3D.cone, p, sc, co, true

    let spin =
      if spins then
        Raymath.MatrixRotateY(elapsed * rotationSpeed)
      else
        Raymath.MatrixIdentity()

    let transform =
      Raymath.MatrixMultiply(
        Raymath.MatrixMultiply(
          Raymath.MatrixScale(scale.X, scale.Y, scale.Z),
          spin
        ),
        Raymath.MatrixTranslate(position.X, position.Y, position.Z)
      )

    let material = Material3D.colored(Mibo.Color.op_Implicit color)
    buffer.mesh(mesh, transform, material) |> ignore

  /// The 3D pass: opens the camera block, registers lights, draws the
  /// primitives and line3D demos, closes the camera block.
  let draw3D
    (_ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer3D)
    : unit =
    match frame.Screen with
    | Screen.Shapes2D ->
      // No 3D geometry on the 2D screen, but the frame must still be
      // cleared before the noClear 2D pass draws: begin a full-screen
      // camera with the sky clear and draw nothing.
      buffer
        .beginCameraWith(Camera3D.render camera |> Camera3D.withClear skyColor)
        .endCamera()
        .drop()
    | Screen.Shapes3D
    | Screen.Split ->
      let cameraConfig =
        match frame.Screen with
        | Screen.Split -> Camera3D.splitScreenRight camera skyColor
        | _ -> Camera3D.render camera |> Camera3D.withClear skyColor

      buffer.beginCameraWith(cameraConfig) |> ignore

      buffer
        .setAmbientLight(
          {
            Color = Mibo.Color.rgb 210uy 215uy 225uy
            Intensity = 0.55f
          }
        )
        .addDirectionalLight(
          {
            Direction = Vector3.Normalize(Vector3(0.5f, -1f, 0.4f))
            Color = Mibo.Color.rgb 255uy 250uy 235uy
            Intensity = 1.1f
            CastsShadows = false
          }
        )
        .drop()

      for s in Layout3D.shapes do
        drawShape frame.Elapsed buffer s

      for d in Layout3D.lines do
        buffer.line3D(d.Start, d.Finish, d.Color) |> ignore

      buffer.endCamera() |> ignore

  /// The 3D HUD pass (Shapes3D only): static 3D labels plus the
  /// title/help, all screen-space.
  let hud3D
    (_ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    : unit =
    let font = Fonts.hud()

    for name, anchor in Layout3D.labels do
      buffer.text(
        font,
        name,
        Vector2(MathF.Round anchor.X, MathF.Round anchor.Y),
        14f,
        layer = Layers.Labels
      )
      |> ignore

    buffer
      .text(
        font,
        Hud.title frame.Screen,
        Vector2(12f, 10f),
        22f,
        layer = Layers.Labels
      )
      .text(font, Hud.help, Vector2(850f, 12f), 16f, layer = Layers.Labels)
      .drop()
