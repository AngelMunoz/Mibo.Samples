namespace PrimitiveGallery.MonoGame

open System
open System.Numerics
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open PrimitiveGallery

// ─────────────────────────────────────────────────────────────
// Screen2DView — the 2D shapes pass. Reads Layout2D.cells (full
// screen) or Layout2D.splitCells (left half in Split mode) and
// dispatches each Shape2D case to the Mibo.Elmish.Graphics fluent
// DSL member of the same name. A dim panel sits behind every cell
// (layer -1), shapes draw on layer 0, and each shape's name is
// labelled with the shared Monogram spritefont (Layers.Labels).
// ─────────────────────────────────────────────────────────────

module Screen2DView =

  /// Dark gray panel drawn behind every cell so shapes read against
  /// the un-cleared backbuffer (the 2D pass is noClear).
  let private panel = Mibo.Color.rgb 30uy 30uy 36uy

  /// Darker backdrop for the un-cleared left half in Split mode (the 3D
  /// pass only clears the right half). Drawn at layer -2, below the cell
  /// panels (-1).
  let private splitBackground = Mibo.Color.rgb 24uy 26uy 32uy

  /// MonoGame's LineStrip/TriangleFan/TriangleStrip witnesses take an
  /// XNA Vector2[] (the array is a backend handle); the shared contract
  /// hands us System.Numerics.Vector2[], so convert once per shape.
  let private toXna(points: Vector2[]) : Microsoft.Xna.Framework.Vector2[] =
    points |> Array.map(fun p -> Microsoft.Xna.Framework.Vector2(p.X, p.Y))

  /// Axis-aligned bounding box of a point list (the panel and label
  /// anchor for point-based shapes).
  let private bounds(points: Vector2[]) : CellRect =
    let mutable minX = Single.MaxValue
    let mutable minY = Single.MaxValue
    let mutable maxX = Single.MinValue
    let mutable maxY = Single.MinValue

    for p in points do
      if p.X < minX then
        minX <- p.X

      if p.Y < minY then
        minY <- p.Y

      if p.X > maxX then
        maxX <- p.X

      if p.Y > maxY then
        maxY <- p.Y

    {
      X = minX
      Y = minY
      W = maxX - minX
      H = maxY - minY
    }

  /// Reconstructs a regular polygon (center, sides, radius, rotation)
  /// from an arbitrary point list. The DSL's fillPoly/polyOutline only
  /// draw REGULAR polygons, so the shared FillPoly/PolyOutline points
  /// (a pentagon) are reduced to their centroid/circumradius. Rotation
  /// is the first point's angle from the centroid in DEGREES (both
  /// backends interpret the rotation parameter as degrees), so the
  /// catalog pentagon keeps its vertex-at-top orientation.
  let private regularOf(points: Vector2[]) : Vector2 * int * float32 * float32 =
    if points.Length = 0 then
      Vector2.Zero, 0, 0f, 0f
    else
      let mutable sx = 0f
      let mutable sy = 0f

      for p in points do
        sx <- sx + p.X
        sy <- sy + p.Y

      let center =
        Vector2(sx / float32 points.Length, sy / float32 points.Length)

      let mutable radius = 0f

      for p in points do
        let d = Vector2.Distance(center, p)

        if d > radius then
          radius <- d

      let d0 = points[0] - center
      let rotation = MathF.Atan2(d0.Y, d0.X) * 180f / MathF.PI
      center, max 3 points.Length, radius, rotation

  let private panelAt
    (buffer: RenderBuffer2D)
    (x: float32)
    (y: float32)
    (w: float32)
    (h: float32)
    =
    buffer.fillRect(x, y, w, h, panel, layer = Layers.Panel) |> ignore

  let private labelAt
    (buffer: RenderBuffer2D)
    (font: SpriteFont)
    (name: string)
    (x: float32)
    (y: float32)
    =
    buffer.text(
      font,
      name,
      Vector2(MathF.Round x, MathF.Round y),
      1.0f,
      layer = Layers.Labels
    )
    |> ignore

  /// Dispatches one Shape2D case to the fluent DSL member of the same
  /// name. Deviations from the shared contract (noted in the report):
  ///  * circleOutline/ellipseOutline have no thickness in the DSL;
  ///  * fillRing/ringOutline need start/end angles (full circle here);
  ///  * bezier is quadratic in the DSL (cubic p2 is dropped);
  ///  * fillPoly/polyOutline draw regular polygons (points reduced);
  ///  * lineStrip/triangleFan/triangleStrip take XNA Vector2[].
  let private drawShape
    (buffer: RenderBuffer2D)
    (font: SpriteFont)
    (shape: Shape2D)
    : unit =
    match shape with
    | Shape2D.FillRect(name, r, color) ->
      panelAt buffer r.X r.Y r.W r.H
      buffer.fillRect(r.X, r.Y, r.W, r.H, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.RectOutline(name, r, color, thickness) ->
      panelAt buffer r.X r.Y r.W r.H

      buffer.rectOutline(r.X, r.Y, r.W, r.H, color, thickness = thickness)
      |> ignore

      labelAt buffer font name r.X r.Y
    | Shape2D.FillRectRounded(name, r, color, roundness, segments) ->
      panelAt buffer r.X r.Y r.W r.H

      buffer.fillRectRounded(
        r.X,
        r.Y,
        r.W,
        r.H,
        color,
        roundness = roundness,
        segments = segments
      )
      |> ignore

      labelAt buffer font name r.X r.Y
    | Shape2D.RectRoundedOutline(name, r, color, roundness, thickness) ->
      panelAt buffer r.X r.Y r.W r.H

      buffer.rectRoundedOutline(
        r.X,
        r.Y,
        r.W,
        r.H,
        color,
        roundness = roundness,
        thickness = thickness
      )
      |> ignore

      labelAt buffer font name r.X r.Y
    | Shape2D.RectGradientV(name, r, top, bottom) ->
      panelAt buffer r.X r.Y r.W r.H

      buffer.rectGradientV(int r.X, int r.Y, int r.W, int r.H, top, bottom)
      |> ignore

      labelAt buffer font name r.X r.Y
    | Shape2D.RectGradientH(name, r, left, right) ->
      panelAt buffer r.X r.Y r.W r.H

      buffer.rectGradientH(int r.X, int r.Y, int r.W, int r.H, left, right)
      |> ignore

      labelAt buffer font name r.X r.Y
    | Shape2D.RectGradient(name, r, c0, c1, c2, c3) ->
      panelAt buffer r.X r.Y r.W r.H
      buffer.rectGradient(r.X, r.Y, r.W, r.H, c0, c1, c2, c3) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.FillCircle(name, c, radius, color) ->
      panelAt buffer (c.X - radius) (c.Y - radius) (radius * 2f) (radius * 2f)
      buffer.fillCircle(c, radius, color) |> ignore
      labelAt buffer font name (c.X - radius) (c.Y - radius)
    | Shape2D.CircleOutline(name, c, radius, color, _thickness) ->
      panelAt buffer (c.X - radius) (c.Y - radius) (radius * 2f) (radius * 2f)
      buffer.circleOutline(c, radius, color) |> ignore
      labelAt buffer font name (c.X - radius) (c.Y - radius)
    | Shape2D.CircleSector(name,
                           c,
                           radius,
                           startAngle,
                           endAngle,
                           color,
                           segments) ->
      panelAt buffer (c.X - radius) (c.Y - radius) (radius * 2f) (radius * 2f)

      buffer.circleSector(
        c,
        radius,
        startAngle,
        endAngle,
        color,
        segments = segments
      )
      |> ignore

      labelAt buffer font name (c.X - radius) (c.Y - radius)
    | Shape2D.CircleSectorOutline(name,
                                  c,
                                  radius,
                                  startAngle,
                                  endAngle,
                                  color,
                                  segments) ->
      panelAt buffer (c.X - radius) (c.Y - radius) (radius * 2f) (radius * 2f)

      buffer.circleSectorOutline(
        c,
        radius,
        startAngle,
        endAngle,
        color,
        segments = segments
      )
      |> ignore

      labelAt buffer font name (c.X - radius) (c.Y - radius)
    | Shape2D.CircleGradient(name, c, radius, inner, outer) ->
      panelAt buffer (c.X - radius) (c.Y - radius) (radius * 2f) (radius * 2f)
      buffer.circleGradient(int c.X, int c.Y, radius, inner, outer) |> ignore
      labelAt buffer font name (c.X - radius) (c.Y - radius)
    | Shape2D.FillRing(name, c, innerRadius, outerRadius, color, segments) ->
      panelAt
        buffer
        (c.X - outerRadius)
        (c.Y - outerRadius)
        (outerRadius * 2f)
        (outerRadius * 2f)

      buffer.fillRing(
        c,
        innerRadius,
        outerRadius,
        0f,
        360f,
        color,
        segments = segments
      )
      |> ignore

      labelAt buffer font name (c.X - outerRadius) (c.Y - outerRadius)
    | Shape2D.RingOutline(name, c, innerRadius, outerRadius, color, segments) ->
      panelAt
        buffer
        (c.X - outerRadius)
        (c.Y - outerRadius)
        (outerRadius * 2f)
        (outerRadius * 2f)

      buffer.ringOutline(
        c,
        innerRadius,
        outerRadius,
        0f,
        360f,
        color,
        segments = segments
      )
      |> ignore

      labelAt buffer font name (c.X - outerRadius) (c.Y - outerRadius)
    | Shape2D.FillEllipse(name, c, radiusX, radiusY, color) ->
      panelAt
        buffer
        (c.X - radiusX)
        (c.Y - radiusY)
        (radiusX * 2f)
        (radiusY * 2f)

      buffer.fillEllipse(int c.X, int c.Y, radiusX, radiusY, color) |> ignore
      labelAt buffer font name (c.X - radiusX) (c.Y - radiusY)
    | Shape2D.EllipseOutline(name, c, radiusX, radiusY, color, _thickness) ->
      panelAt
        buffer
        (c.X - radiusX)
        (c.Y - radiusY)
        (radiusX * 2f)
        (radiusY * 2f)

      buffer.ellipseOutline(int c.X, int c.Y, radiusX, radiusY, color) |> ignore
      labelAt buffer font name (c.X - radiusX) (c.Y - radiusY)
    | Shape2D.Line(name, a, b, color) ->
      let r = bounds [| a; b |]
      panelAt buffer r.X r.Y r.W r.H
      buffer.line(a, b, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.LineThick(name, a, b, color, thickness) ->
      let r = bounds [| a; b |]
      panelAt buffer r.X r.Y r.W r.H
      buffer.lineThick(a, b, color, thickness = thickness) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.LineStrip(name, points, color) ->
      let r = bounds points
      panelAt buffer r.X r.Y r.W r.H
      buffer.lineStrip(toXna points, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.Bezier(name, p0, p1, p2, p3, color, thickness) ->
      let r = bounds [| p0; p1; p2; p3 |]
      panelAt buffer r.X r.Y r.W r.H
      buffer.bezier(p0, p1, p3, color, thickness = thickness) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.Triangle(name, a, b, c, color) ->
      let r = bounds [| a; b; c |]
      panelAt buffer r.X r.Y r.W r.H
      buffer.triangle(a, b, c, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.TriangleFan(name, points, color) ->
      let r = bounds points
      panelAt buffer r.X r.Y r.W r.H
      buffer.triangleFan(toXna points, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.TriangleStrip(name, points, color) ->
      let r = bounds points
      panelAt buffer r.X r.Y r.W r.H
      buffer.triangleStrip(toXna points, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.FillPoly(name, points, color) ->
      let center, sides, radius, rotation = regularOf points
      let r = bounds points
      panelAt buffer r.X r.Y r.W r.H
      buffer.fillPoly(center, sides, radius, rotation, color) |> ignore
      labelAt buffer font name r.X r.Y
    | Shape2D.PolyOutline(name, points, color, thickness) ->
      let center, sides, radius, rotation = regularOf points
      let r = bounds points
      panelAt buffer r.X r.Y r.W r.H

      buffer.polyOutline(
        center,
        sides,
        radius,
        rotation,
        color,
        thickness = thickness
      )
      |> ignore

      labelAt buffer font name r.X r.Y

  /// The 2D shapes pass. Full 5x5 grid for Shapes2D, left half for
  /// Split, nothing for Shapes3D (the 3D renderer owns that screen).
  let draw2D
    (ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    : unit =
    // Pixel-perfect text: the Monogram spritefont is a pixel font, so
    // point-sampled glyphs stay crisp (text is the only textured draw).
    buffer.setSamplerState(SamplerState.PointClamp, layer = 0<RenderLayer>)
    |> ignore

    // The 3D pass only clears the right half in Split mode; the left
    // half is never cleared, so paint a dark backdrop (below the cell
    // panels at -1) for the re-laid grid.
    match frame.Screen with
    | Screen.Split ->
      buffer.fillRect(
        0f,
        0f,
        640f,
        720f,
        splitBackground,
        layer = Layers.Backdrop
      )
      |> ignore
    | _ -> ()

    let shapes =
      match frame.Screen with
      | Screen.Shapes2D -> Layout2D.cells
      | Screen.Split -> Layout2D.splitCells
      | Screen.Shapes3D -> [||]

    if shapes.Length > 0 then
      let assets = GameContext.getService<IAssets> ctx
      let font = assets.Font Paths.Font

      for shape in shapes do
        drawShape buffer font shape
