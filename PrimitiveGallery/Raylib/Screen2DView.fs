namespace PrimitiveGallery.Raylib

open System
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs
open PrimitiveGallery

// ─────────────────────────────────────────────────────────────
// Screen2DView — the 2D shapes pass. Draws the Layout2D grid
// (full-screen for Shapes2D, re-laid into the left half for Split)
// plus the 2D-only HUD (title + help). Reads ONLY the forced
// RenderFrame.
//
// Every Shape2D case maps to the fluent Draw DSL member of the same
// name on the RenderBuffer2D (Mibo.Elmish.Graphics.Draw extension
// members — NOT the deprecated Mibo.Elmish.Graphics2D.Draw module).
// ─────────────────────────────────────────────────────────────

/// Render-layer constants for the 2D pass. Invariant: every TEXT draw
/// uses Layers.Labels — the top layer — so text always renders in
/// front of the shapes (0), panels (Panel), and the split backdrop.
module Layers =

  [<Literal>]
  let Backdrop = -2<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Panel = -1<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Labels = 1000<Mibo.Elmish.Graphics2D.RenderLayer>

/// The default raylib font with point filtering applied ONCE on first
/// use — the window/GL context only exists after the first frame
/// starts, so the filter cannot be set at boot (it would crash).
module Fonts =

  let mutable private crisp = false

  let hud() : Font =
    let font = Raylib.GetFontDefault()

    if not crisp then
      Raylib.SetTextureFilter(font.Texture, TextureFilter.Point)
      crisp <- true

    font

module Screen2DView =

  /// Dark gray background panel drawn behind every cell so the grid reads
  /// clearly (shapes draw at layer 0 on top of it).
  let private panelColor = Mibo.Color.rgb 38uy 40uy 48uy

  /// Darker backdrop for the un-cleared left half in Split mode (the 3D
  /// pass only clears the right half). Drawn at layer -2, below the cell
  /// panels (-1).
  let private splitBackground = Mibo.Color.rgb 24uy 26uy 32uy

  let private circleRect (c: Vector2) (radius: float32) : CellRect = {
    X = c.X - radius
    Y = c.Y - radius
    W = radius * 2f
    H = radius * 2f
  }

  let private ellipseRect (c: Vector2) (rx: float32) (ry: float32) : CellRect = {
    X = c.X - rx
    Y = c.Y - ry
    W = rx * 2f
    H = ry * 2f
  }

  let private rectOfPoints(pts: Vector2[]) : CellRect =
    if pts.Length = 0 then
      { X = 0f; Y = 0f; W = 0f; H = 0f }
    else
      let mutable minX = pts[0].X
      let mutable minY = pts[0].Y
      let mutable maxX = pts[0].X
      let mutable maxY = pts[0].Y

      for p in pts do
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

  /// (name, cell bounds) for every shape — one match feeding both the label
  /// and the background panel. Point/line shapes derive a tight bounding box.
  let private describe(s: Shape2D) : string * CellRect =
    match s with
    | Shape2D.FillRect(name, r, _) -> name, r
    | Shape2D.RectOutline(name, r, _, _) -> name, r
    | Shape2D.FillRectRounded(name, r, _, _, _) -> name, r
    | Shape2D.RectRoundedOutline(name, r, _, _, _) -> name, r
    | Shape2D.RectGradientV(name, r, _, _) -> name, r
    | Shape2D.RectGradientH(name, r, _, _) -> name, r
    | Shape2D.RectGradient(name, r, _, _, _, _) -> name, r
    | Shape2D.FillCircle(name, c, radius, _) -> name, circleRect c radius
    | Shape2D.CircleOutline(name, c, radius, _, _) -> name, circleRect c radius
    | Shape2D.CircleSector(name, c, radius, _, _, _, _) ->
      name, circleRect c radius
    | Shape2D.CircleSectorOutline(name, c, radius, _, _, _, _) ->
      name, circleRect c radius
    | Shape2D.CircleGradient(name, c, radius, _, _) -> name, circleRect c radius
    | Shape2D.FillRing(name, c, _, outer, _, _) -> name, circleRect c outer
    | Shape2D.RingOutline(name, c, _, outer, _, _) -> name, circleRect c outer
    | Shape2D.FillEllipse(name, c, rx, ry, _) -> name, ellipseRect c rx ry
    | Shape2D.EllipseOutline(name, c, rx, ry, _, _) -> name, ellipseRect c rx ry
    | Shape2D.Line(name, a, b, _) -> name, rectOfPoints [| a; b |]
    | Shape2D.LineThick(name, a, b, _, _) -> name, rectOfPoints [| a; b |]
    | Shape2D.LineStrip(name, pts, _) -> name, rectOfPoints pts
    | Shape2D.Bezier(name, p0, _, _, p3, _, _) ->
      name, rectOfPoints [| p0; p3 |]
    | Shape2D.Triangle(name, a, b, c, _) -> name, rectOfPoints [| a; b; c |]
    | Shape2D.TriangleFan(name, pts, _) -> name, rectOfPoints pts
    | Shape2D.TriangleStrip(name, pts, _) -> name, rectOfPoints pts
    | Shape2D.FillPoly(name, pts, _) -> name, rectOfPoints pts
    | Shape2D.PolyOutline(name, pts, _, _) -> name, rectOfPoints pts

  /// Collapses an arbitrary point polygon into the regular-polygon form the
  /// Draw.fillPoly / Draw.polyOutline members take (center, sides, radius,
  /// rotation). Both backends treat `rotation` as DEGREES measured from +X,
  /// so the angle of the first contract point from the centroid is returned
  /// in degrees — this keeps the catalog pentagon's vertex-at-top orientation
  /// identical on raylib and MonoGame. Radius is the circumradius (max vertex
  /// distance), matching the contract points.
  let private regularPolygonOf
    (pts: Vector2[])
    : Vector2 * int * float32 * float32 =
    if pts.Length = 0 then
      Vector2.Zero, 0, 0f, 0f
    else
      let mutable sx = 0f
      let mutable sy = 0f

      for p in pts do
        sx <- sx + p.X
        sy <- sy + p.Y

      let center = Vector2(sx / float32 pts.Length, sy / float32 pts.Length)
      let mutable radius = 0f

      for p in pts do
        let d = Vector2.Distance(center, p)

        if d > radius then
          radius <- d

      let d0 = pts[0] - center
      let rotation = MathF.Atan2(d0.Y, d0.X) * 180f / MathF.PI
      center, max 3 pts.Length, radius, rotation

  /// Draws a single shape (no panel, no label) at layer 0.
  let private drawShape (buffer: RenderBuffer2D) (s: Shape2D) : unit =
    match s with
    | Shape2D.FillRect(_, r, color) ->
      buffer.fillRect(r.X, r.Y, r.W, r.H, color, layer = 0<RenderLayer>)
      |> ignore
    | Shape2D.RectOutline(_, r, color, thickness) ->
      buffer.rectOutline(
        r.X,
        r.Y,
        r.W,
        r.H,
        color,
        thickness = thickness,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.FillRectRounded(_, r, color, roundness, segments) ->
      buffer.fillRectRounded(
        r.X,
        r.Y,
        r.W,
        r.H,
        color,
        roundness = roundness,
        segments = segments,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.RectRoundedOutline(_, r, color, roundness, thickness) ->
      buffer.rectRoundedOutline(
        r.X,
        r.Y,
        r.W,
        r.H,
        color,
        roundness = roundness,
        thickness = thickness,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.RectGradientV(_, r, top, bottom) ->
      buffer.rectGradientV(
        int r.X,
        int r.Y,
        int r.W,
        int r.H,
        top,
        bottom,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.RectGradientH(_, r, left, right) ->
      buffer.rectGradientH(
        int r.X,
        int r.Y,
        int r.W,
        int r.H,
        left,
        right,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.RectGradient(_, r, c0, c1, c2, c3) ->
      buffer.rectGradient(
        r.X,
        r.Y,
        r.W,
        r.H,
        c0,
        c1,
        c2,
        c3,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.FillCircle(_, c, radius, color) ->
      buffer.fillCircle(c, radius, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.CircleOutline(_, c, radius, color, _) ->
      buffer.circleOutline(c, radius, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.CircleSector(_, c, radius, startAngle, endAngle, color, segments) ->
      buffer.circleSector(
        c,
        radius,
        startAngle,
        endAngle,
        color,
        segments = segments,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.CircleSectorOutline(_,
                                  c,
                                  radius,
                                  startAngle,
                                  endAngle,
                                  color,
                                  segments) ->
      buffer.circleSectorOutline(
        c,
        radius,
        startAngle,
        endAngle,
        color,
        segments = segments,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.CircleGradient(_, c, radius, inner, outer) ->
      buffer.circleGradient(
        int c.X,
        int c.Y,
        radius,
        inner,
        outer,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.FillRing(_, c, innerR, outerR, color, segments) ->
      buffer.fillRing(
        c,
        innerR,
        outerR,
        0f,
        360f,
        color,
        segments = segments,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.RingOutline(_, c, innerR, outerR, color, segments) ->
      buffer.ringOutline(
        c,
        innerR,
        outerR,
        0f,
        360f,
        color,
        segments = segments,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.FillEllipse(_, c, rx, ry, color) ->
      buffer.fillEllipse(
        int c.X,
        int c.Y,
        rx,
        ry,
        color,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.EllipseOutline(_, c, rx, ry, color, _) ->
      buffer.ellipseOutline(
        int c.X,
        int c.Y,
        rx,
        ry,
        color,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.Line(_, a, b, color) ->
      buffer.line(a, b, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.LineThick(_, a, b, color, thickness) ->
      buffer.lineThick(
        a,
        b,
        color,
        thickness = thickness,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.LineStrip(_, pts, color) ->
      buffer.lineStrip(pts, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.Bezier(_, p0, p1, _, p3, color, thickness) ->
      buffer.bezier(
        p0,
        p1,
        p3,
        color,
        thickness = thickness,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.Triangle(_, a, b, c, color) ->
      buffer.triangle(a, b, c, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.TriangleFan(_, pts, color) ->
      buffer.triangleFan(pts, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.TriangleStrip(_, pts, color) ->
      buffer.triangleStrip(pts, color, layer = 0<RenderLayer>) |> ignore
    | Shape2D.FillPoly(_, pts, color) ->
      let center, sides, radius, rotation = regularPolygonOf pts

      buffer.fillPoly(
        center,
        sides,
        radius,
        rotation,
        color,
        layer = 0<RenderLayer>
      )
      |> ignore
    | Shape2D.PolyOutline(_, pts, color, thickness) ->
      let center, sides, radius, rotation = regularPolygonOf pts

      buffer.polyOutline(
        center,
        sides,
        radius,
        rotation,
        color,
        thickness = thickness,
        layer = 0<RenderLayer>
      )
      |> ignore

  /// Panel + shape + label for one cell.
  let private drawCell (buffer: RenderBuffer2D) (s: Shape2D) : unit =
    let name, bounds = describe s

    buffer.fillRect(
      bounds.X - 4f,
      bounds.Y - 4f,
      bounds.W + 8f,
      bounds.H + 8f,
      panelColor,
      layer = Layers.Panel
    )
    |> ignore

    drawShape buffer s

    buffer.text(
      Fonts.hud(),
      name,
      Vector2(
        MathF.Round(bounds.X + 4f),
        MathF.Round(bounds.Y + bounds.H - 18f)
      ),
      14f,
      layer = Layers.Labels
    )
    |> ignore

  let private drawCells (buffer: RenderBuffer2D) (cells: Shape2D[]) : unit =
    for s in cells do
      drawCell buffer s

  /// The 2D shapes pass: Shapes2D draws the full-screen grid, Split draws the
  /// same shapes re-laid into the left half. Shapes3D draws nothing here.
  let draw2D
    (_ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    : unit =
    match frame.Screen with
    | Screen.Shapes3D -> ()
    | Screen.Shapes2D -> drawCells buffer Layout2D.cells
    | Screen.Split ->
      // The 3D pass only clears the right half (splitScreenRight); the
      // left half is never cleared, so paint a dark backdrop (below the
      // cell panels at -1) for the re-laid grid.
      buffer.fillRect(
        0f,
        0f,
        640f,
        720f,
        splitBackground,
        layer = Layers.Backdrop
      )
      |> ignore

      drawCells buffer Layout2D.splitCells

  /// The 2D-only HUD: title and help, no 3D labels.
  let hud2D
    (_ctx: GameContext)
    (frame: Frame.RenderFrame)
    (buffer: RenderBuffer2D)
    : unit =
    let font = Fonts.hud()

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
