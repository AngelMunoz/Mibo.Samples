namespace PrimitiveGallery

open System.Numerics

type CellRect = {
  X: float32
  Y: float32
  W: float32
  H: float32
}

[<RequireQualifiedAccess>]
type Shape2D =
  | FillRect of name: string * rect: CellRect * color: Mibo.Color
  | RectOutline of
    name: string *
    rect: CellRect *
    color: Mibo.Color *
    thickness: float32
  | FillRectRounded of
    name: string *
    rect: CellRect *
    color: Mibo.Color *
    roundness: float32 *
    segments: int
  | RectRoundedOutline of
    name: string *
    rect: CellRect *
    color: Mibo.Color *
    roundness: float32 *
    thickness: float32
  | RectGradientV of
    name: string *
    rect: CellRect *
    top: Mibo.Color *
    bottom: Mibo.Color
  | RectGradientH of
    name: string *
    rect: CellRect *
    left: Mibo.Color *
    right: Mibo.Color
  | RectGradient of
    name: string *
    rect: CellRect *
    c0: Mibo.Color *
    c1: Mibo.Color *
    c2: Mibo.Color *
    c3: Mibo.Color
  | FillCircle of
    name: string *
    center: Vector2 *
    radius: float32 *
    color: Mibo.Color
  | CircleOutline of
    name: string *
    center: Vector2 *
    radius: float32 *
    color: Mibo.Color *
    thickness: float32
  | CircleSector of
    name: string *
    center: Vector2 *
    radius: float32 *
    startAngle: float32 *
    endAngle: float32 *
    color: Mibo.Color *
    segments: int
  | CircleSectorOutline of
    name: string *
    center: Vector2 *
    radius: float32 *
    startAngle: float32 *
    endAngle: float32 *
    color: Mibo.Color *
    segments: int
  | CircleGradient of
    name: string *
    center: Vector2 *
    radius: float32 *
    inner: Mibo.Color *
    outer: Mibo.Color
  | FillRing of
    name: string *
    center: Vector2 *
    innerRadius: float32 *
    outerRadius: float32 *
    color: Mibo.Color *
    segments: int
  | RingOutline of
    name: string *
    center: Vector2 *
    innerRadius: float32 *
    outerRadius: float32 *
    color: Mibo.Color *
    segments: int
  | FillEllipse of
    name: string *
    center: Vector2 *
    radiusX: float32 *
    radiusY: float32 *
    color: Mibo.Color
  | EllipseOutline of
    name: string *
    center: Vector2 *
    radiusX: float32 *
    radiusY: float32 *
    color: Mibo.Color *
    thickness: float32
  | Line of name: string * a: Vector2 * b: Vector2 * color: Mibo.Color
  | LineThick of
    name: string *
    a: Vector2 *
    b: Vector2 *
    color: Mibo.Color *
    thickness: float32
  | LineStrip of name: string * points: Vector2[] * color: Mibo.Color
  | Bezier of
    name: string *
    p0: Vector2 *
    p1: Vector2 *
    p2: Vector2 *
    p3: Vector2 *
    color: Mibo.Color *
    thickness: float32
  | Triangle of
    name: string *
    a: Vector2 *
    b: Vector2 *
    c: Vector2 *
    color: Mibo.Color
  | TriangleFan of name: string * points: Vector2[] * color: Mibo.Color
  | TriangleStrip of name: string * points: Vector2[] * color: Mibo.Color
  | FillPoly of name: string * points: Vector2[] * color: Mibo.Color
  | PolyOutline of
    name: string *
    points: Vector2[] *
    color: Mibo.Color *
    thickness: float32

[<RequireQualifiedAccess>]
type Shape3D =
  | Cube of
    name: string *
    position: Vector3 *
    scale: Vector3 *
    color: Mibo.Color
  | Sphere of
    name: string *
    position: Vector3 *
    scale: Vector3 *
    color: Mibo.Color
  | Cylinder of
    name: string *
    position: Vector3 *
    scale: Vector3 *
    color: Mibo.Color
  | Plane of
    name: string *
    position: Vector3 *
    scale: Vector3 *
    color: Mibo.Color
  | Torus of
    name: string *
    position: Vector3 *
    scale: Vector3 *
    color: Mibo.Color
  | Cone of
    name: string *
    position: Vector3 *
    scale: Vector3 *
    color: Mibo.Color

type Line3DDemo = {
  Name: string
  Start: Vector3
  Finish: Vector3
  Color: Mibo.Color
}

/// The three screens the sample switches between. Defined in Catalog.fs so that
/// the layout and HUD modules (below) and the state/frame/input files can share
/// the single type without a forward reference.
[<RequireQualifiedAccess>]
type Screen =
  | Shapes2D
  | Shapes3D
  | Split

module Layout2D =

  let private header = 40f

  let private orange = Mibo.Color.rgb 255uy 165uy 0uy
  let private yellow = Mibo.Color.rgb 255uy 255uy 0uy
  let private purple = Mibo.Color.rgb 128uy 0uy 128uy
  let private cyan = Mibo.Color.rgb 0uy 255uy 255uy
  let private magenta = Mibo.Color.rgb 255uy 0uy 255uy
  let private pink = Mibo.Color.rgb 255uy 105uy 180uy
  let private teal = Mibo.Color.rgb 0uy 128uy 128uy
  let private lime = Mibo.Color.rgb 50uy 205uy 50uy
  let private gold = Mibo.Color.rgb 255uy 215uy 0uy

  /// Builds the 25-shape 5x5 grid for a given column width. Cell geometry is
  /// derived from the column width and radius so the same catalogue can be
  /// re-laid inside the left half without overlapping.
  let private build
    (colW: float32)
    (rectW: float32)
    (rectH: float32)
    (radius: float32)
    : Shape2D[] =
    let rowH = (720f - header) / 5f
    let k = radius / 40f

    let cx col = colW * (float32 col + 0.5f)
    let cy row = header + rowH * (float32 row + 0.5f)

    let center col row = Vector2(cx col, cy row)

    let rect col row = {
      X = cx col - rectW * 0.5f
      Y = cy row - rectH * 0.5f
      W = rectW
      H = rectH
    }

    let rel (c: Vector2) (x: float32) (y: float32) =
      Vector2(c.X + x * k, c.Y + y * k)

    let pentagon c = [|
      rel c 0f -40f
      rel c -38f -12f
      rel c -24f 32f
      rel c 24f 32f
      rel c 38f -12f
    |]

    // Fan demo: hub + open 180° arc whose rim alternates radius (38/20) so
    // every triangle has a visible outer edge — a uniform-radius arc reads
    // as a plain semicircle and hides the fan's triangles. Increasing angle
    // runs clockwise on this Y-down screen, so the rim stays the winding
    // regression check.
    let fanArc c =
      let radii = [| 38f; 20f; 38f; 20f; 38f; 20f; 38f |]
      let start = 180f * System.MathF.PI / 180f
      let step = 30f * System.MathF.PI / 180f

      [|
        for i = 0 to 6 do
          let a = start + float32 i * step

          rel
            c
            (System.MathF.Cos(a) * radii[i])
            (System.MathF.Sin(a) * radii[i])
      |]

    let fanCell c = Array.append [| c |] (fanArc c)

    [|
      Shape2D.FillRect("fillRect", rect 0 0, Mibo.Color.Red)
      Shape2D.RectOutline("rectOutline", rect 1 0, Mibo.Color.Blue, 3f)
      Shape2D.FillRectRounded(
        "fillRectRounded",
        rect 2 0,
        Mibo.Color.Green,
        0.5f,
        12
      )
      Shape2D.RectRoundedOutline(
        "rectRoundedOutline",
        rect 3 0,
        orange,
        0.5f,
        3f
      )
      Shape2D.RectGradientV("rectGradientV", rect 4 0, yellow, purple)
      Shape2D.RectGradientH("rectGradientH", rect 0 1, cyan, magenta)
      Shape2D.RectGradient(
        "rectGradient",
        rect 1 1,
        Mibo.Color.Red,
        Mibo.Color.Green,
        Mibo.Color.Blue,
        Mibo.Color.White
      )
      Shape2D.FillCircle("fillCircle", center 2 1, radius, pink)
      Shape2D.CircleOutline("circleOutline", center 3 1, radius, teal, 3f)
      Shape2D.CircleSector(
        "circleSector",
        center 4 1,
        radius,
        30f,
        210f,
        orange,
        20
      )
      Shape2D.CircleSectorOutline(
        "circleSectorOutline",
        center 0 2,
        radius,
        30f,
        210f,
        purple,
        20
      )
      Shape2D.CircleGradient(
        "circleGradient",
        center 1 2,
        radius,
        Mibo.Color.White,
        Mibo.Color.Blue
      )
      Shape2D.FillRing("fillRing", center 2 2, radius * 0.5f, radius, lime, 24)
      Shape2D.RingOutline(
        "ringOutline",
        center 3 2,
        radius * 0.5f,
        radius,
        Mibo.Color.Red,
        24
      )
      Shape2D.FillEllipse(
        "fillEllipse",
        center 4 2,
        radius * 1.4f,
        radius * 0.8f,
        gold
      )
      Shape2D.EllipseOutline(
        "ellipseOutline",
        center 0 3,
        radius * 1.4f,
        radius * 0.8f,
        Mibo.Color.Blue,
        3f
      )
      Shape2D.Line(
        "line",
        rel (center 1 3) -60f -20f,
        rel (center 1 3) 60f 20f,
        Mibo.Color.White
      )
      Shape2D.LineThick(
        "lineThick",
        rel (center 2 3) -60f 20f,
        rel (center 2 3) 60f -20f,
        Mibo.Color.Red,
        4f
      )
      Shape2D.LineStrip(
        "lineStrip",
        [|
          rel (center 3 3) -60f 20f
          rel (center 3 3) -30f -20f
          rel (center 3 3) 0f 20f
          rel (center 3 3) 30f -20f
          rel (center 3 3) 60f 20f
        |],
        cyan
      )
      Shape2D.Bezier(
        "bezier",
        rel (center 4 3) -70f 20f,
        rel (center 4 3) -30f -40f,
        rel (center 4 3) 30f 40f,
        rel (center 4 3) 70f -20f,
        lime,
        3f
      )
      Shape2D.Triangle(
        "triangle",
        rel (center 0 4) 0f -40f,
        rel (center 0 4) -55f 30f,
        rel (center 0 4) 55f 30f,
        magenta
      )
      // Deliberately CLOCKWISE rim (increasing arc angle, Y-down screen)
      // — filled primitives must render in any winding order on every
      // backend, so this cell doubles as the regression check for that
      // contract.
      Shape2D.TriangleFan("triangleFan", fanCell(center 1 4), orange)
      // Wavy zigzag band: the strip's coverage is visibly narrower than
      // its bounding box, so the triangulation reads at a glance instead
      // of collapsing into a filled rectangle.
      Shape2D.TriangleStrip(
        "triangleStrip",
        [|
          rel (center 2 4) -55f -25f
          rel (center 2 4) -40f 0f
          rel (center 2 4) -15f -35f
          rel (center 2 4) 0f -8f
          rel (center 2 4) 25f -30f
          rel (center 2 4) 40f -2f
        |],
        purple
      )
      Shape2D.FillPoly("fillPoly", pentagon(center 3 4), teal)
      Shape2D.PolyOutline(
        "polyOutline",
        pentagon(center 4 4),
        Mibo.Color.White,
        3f
      )
    |]

  /// All 25 shapes in a 5x5 grid sized for a 1280x720 window (40px HUD band).
  let cells: Shape2D[] = build 256f 160f 80f 40f

  /// The same 25 shapes re-laid inside the left half (x in [0, 640]).
  let splitCells: Shape2D[] = build 128f 96f 72f 32f

module Layout3D =

  let private gray = Mibo.Color.rgb 128uy 128uy 128uy
  let private yellow = Mibo.Color.rgb 255uy 255uy 0uy
  let private magenta = Mibo.Color.rgb 255uy 0uy 255uy
  let private cyan = Mibo.Color.rgb 0uy 255uy 255uy

  /// The six unit primitives on a row, centered at y = 0.75 above the ground
  /// plane (y = 0), plus the ground plane itself.
  let shapes: Shape3D[] = [|
    Shape3D.Cube("cube", Vector3(-7.5f, 0.75f, 0f), Vector3.One, Mibo.Color.Red)
    Shape3D.Sphere(
      "sphere",
      Vector3(-4.5f, 0.75f, 0f),
      Vector3.One,
      Mibo.Color.Green
    )
    Shape3D.Cylinder(
      "cylinder",
      Vector3(-1.5f, 0.75f, 0f),
      Vector3.One,
      Mibo.Color.Blue
    )
    Shape3D.Plane("plane", Vector3(1.5f, 0.75f, 0f), Vector3.One, yellow)
    Shape3D.Torus("torus", Vector3(4.5f, 0.75f, 0f), Vector3.One, magenta)
    Shape3D.Cone("cone", Vector3(7.5f, 0.75f, 0f), Vector3.One, cyan)
    Shape3D.Plane("ground", Vector3(0f, 0f, 0f), Vector3(30f, 1f, 30f), gray)
  |]

  /// Line demos, laid out as a front row (z ≈ 4.5–6.5) in front of the
  /// shape row so each projects BELOW it, clear of the bodies: an RGB axis
  /// tripod on the left, a square loop center-left, and a tall vertical on
  /// the right past the cone. Ground-coplanar points hover at y = 0.1 —
  /// exactly on y = 0 they z-fight the ground plane (drawn first, with
  /// depth writes) and the lines vanish under it.
  let lines: Line3DDemo[] = [|
    {
      Name = "line3D axis"
      Start = Vector3(-6.5f, 0.1f, 4.5f)
      Finish = Vector3(-5f, 0.1f, 4.5f)
      Color = Mibo.Color.Red
    }
    {
      Name = "line3D axis"
      Start = Vector3(-6.5f, 0.1f, 4.5f)
      Finish = Vector3(-6.5f, 1.6f, 4.5f)
      Color = Mibo.Color.Green
    }
    {
      Name = "line3D axis"
      Start = Vector3(-6.5f, 0.1f, 4.5f)
      Finish = Vector3(-6.5f, 0.1f, 6f)
      Color = Mibo.Color.Blue
    }
    {
      Name = "line3D square"
      Start = Vector3(-2.5f, 0.1f, 4.5f)
      Finish = Vector3(-0.5f, 0.1f, 4.5f)
      Color = Mibo.Color.White
    }
    {
      Name = "line3D square"
      Start = Vector3(-0.5f, 0.1f, 4.5f)
      Finish = Vector3(-0.5f, 0.1f, 6.5f)
      Color = Mibo.Color.White
    }
    {
      Name = "line3D square"
      Start = Vector3(-0.5f, 0.1f, 6.5f)
      Finish = Vector3(-2.5f, 0.1f, 6.5f)
      Color = Mibo.Color.White
    }
    {
      Name = "line3D square"
      Start = Vector3(-2.5f, 0.1f, 6.5f)
      Finish = Vector3(-2.5f, 0.1f, 4.5f)
      Color = Mibo.Color.White
    }
    {
      Name = "line3D vertical"
      Start = Vector3(7f, 0.1f, 4.5f)
      Finish = Vector3(7f, 3.2f, 4.5f)
      Color = yellow
    }
  |]

  /// Screen-space label anchors. The clients use a fixed camera, so projected
  /// positions are constant; each label sits at the bottom of the window
  /// directly below its shape/demo (front-row line demos project lower than
  /// the shape row at z = 0).
  let labels: (string * Vector2)[] = [|
    "cube", Vector2(160f, 500f)
    "sphere", Vector2(330f, 500f)
    "cylinder", Vector2(500f, 500f)
    "plane", Vector2(670f, 500f)
    "torus", Vector2(840f, 500f)
    "cone", Vector2(1010f, 500f)
    "ground", Vector2(640f, 570f)
    "line3D axis", Vector2(120f, 640f)
    "line3D square", Vector2(450f, 640f)
    "line3D vertical", Vector2(950f, 640f)
  |]

module Hud =

  let title(screen: Screen) : string =
    match screen with
    | Screen.Shapes2D -> "PrimitiveGallery | 2D Shapes"
    | Screen.Shapes3D -> "PrimitiveGallery | 3D Shapes"
    | Screen.Split -> "PrimitiveGallery | Split"

  let help: string = "1: 2D shapes   2: 3D shapes   3: split"
