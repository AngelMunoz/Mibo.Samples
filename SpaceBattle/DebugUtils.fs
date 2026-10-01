module SpaceBattle.DebugUtils

open System.Numerics
open Mibo
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs

[<Literal>]
let FontSize = 16f

[<Literal>]
let LineHeight = 18

[<Literal>]
let Padding = 10

[<Literal>]
let BgLayer = 0<RenderLayer>

[<Literal>]
let TextLayer = 1<RenderLayer>

[<Struct>]
type DebugStyle = {
  TextColor: Mibo.Color
  SectionColor: Mibo.Color
  BgColor: Mibo.Color
}

let defaultStyle = {
  TextColor = Color.rgb 200uy 200uy 200uy
  SectionColor = Color.rgb 100uy 200uy 255uy
  BgColor = Color.create 0uy 0uy 0uy 180uy
}

let inline private textState
  (font: Raylib_cs.Font)
  (color: Mibo.Color)
  (x: int)
  (y: int)
  (msg: string)
  =
  TextState.create(font, msg, Vector2(float32 x, float32 y))
  |> TextState.withFontSize FontSize
  |> TextState.withColor(RaylibColor.toRaylibColor color)
  |> TextState.withLayer TextLayer

let inline drawText
  (font: Raylib_cs.Font)
  (style: DebugStyle)
  (x: int)
  (y: int)
  (msg: string)
  (buffer: RenderBuffer2D)
  : struct (int * RenderBuffer2D) =
  buffer.text(textState font style.TextColor x y msg) |> ignore

  struct (y + LineHeight, buffer)

let inline section
  (font: Raylib_cs.Font)
  (style: DebugStyle)
  (x: int)
  (y: int)
  (name: string)
  (buffer: RenderBuffer2D)
  : struct (int * RenderBuffer2D) =
  drawText font style x y $"── {name} ──" buffer

let inline kv
  (font: Raylib_cs.Font)
  (style: DebugStyle)
  (x: int)
  (y: int)
  (key: string)
  (value: string)
  (buffer: RenderBuffer2D)
  : struct (int * RenderBuffer2D) =
  drawText font style (x + Padding) y $"{key}: {value}" buffer

let inline background
  (x: int)
  (y: int)
  (w: int)
  (h: int)
  (style: DebugStyle)
  (buffer: RenderBuffer2D)
  : RenderBuffer2D =
  buffer.fillRect(
    float32 x,
    float32 y,
    float32 w,
    float32 h,
    style.BgColor,
    BgLayer
  )

let inline formatCell(struct (col, row): struct (int * int)) = $"({col},{row})"

let inline formatVopt (printer: 'T -> string) (value: 'T voption) =
  match value with
  | ValueSome v -> printer v
  | ValueNone -> "—"
