module LiveMap.Hud

open System.Numerics
open LiveMap
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs

/// One line of text, drawn twice so it stays readable over the map: a dark
/// copy behind, then the text.
let private line
  (font: Font)
  (size: float32)
  (tint: Color)
  (x: float32)
  (y: float32)
  (text: string)
  (buffer: RenderBuffer2D)
  : unit =
  let state (color: Color) (offset: float32) =
    TextState.create(font, text, Vector2(x + offset, y + offset))
    |> TextState.withFontSize size
    |> TextState.withSpacing 1.0f
    |> TextState.withColor color
    |> TextState.withLayer Constants.hudLayer

  buffer.text(state (Color(0uy, 0uy, 0uy, 190uy)) 1.5f).drop()
  buffer.text(state tint 0.0f).drop()

/// What the pointer is over, or nothing when it is off the map.
let private hoverLine(hover: Hover.Info voption) : string =
  hover
  |> ValueOption.map(fun info ->
    let struct (x, y) = info.Cell
    let region = info.Region |> ValueOption.defaultValue "no region"
    let blocks = if info.Solid then "solid" else "walkable"

    $"cell {x},{y}  ·  {info.Layer}  ·  {info.Word}  ·  {blocks}  ·  region {region}")
  |> ValueOption.defaultValue "pointer: off the map"

/// The overlay: what is loaded, what the last build did, what the pointer
/// is over, and the keys.
let view
  (assets: Assets.Assets)
  (document: Document.Model)
  (hover: Hover.Info voption)
  (help: bool)
  (viewport: Vector2)
  (buffer: RenderBuffer2D)
  : unit =
  let font = assets.Font

  buffer.fillRect(
    0.0f,
    0.0f,
    viewport.X,
    96.0f,
    Mibo.Color.create 12uy 14uy 18uy 170uy,
    layer = Constants.hudPanelLayer
  )
  |> ignore

  let white = Color(236uy, 238uy, 242uy, 255uy)
  let dim = Color(168uy, 176uy, 188uy, 255uy)
  let accent = Color(255uy, 214uy, 102uy, 255uy)
  let warn = Color(255uy, 138uy, 128uy, 255uy)

  line
    font
    22.0f
    white
    16.0f
    12.0f
    $"LiveMap · {Mode.label document.Mode} · {Syntax.label document.Syntax}"
    buffer

  line font 18.0f dim 16.0f 40.0f document.Path buffer

  let statusTint = if document.Status.StartsWith "built" then accent else warn

  line font 18.0f statusTint 16.0f 62.0f document.Status buffer

  let footer =
    if help then
      "arrows pan · Q E turn the view · + - zoom · Home reframe · 1 square 2D · 2 blocks 3D · Tab KDL/XML · F1 help · F11 fullscreen · Esc quit"
    else
      "F1 help"

  line font 16.0f dim 16.0f (viewport.Y - 26.0f) footer buffer
  line font 16.0f white 16.0f (viewport.Y - 48.0f) (hoverLine hover) buffer
