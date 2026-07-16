module Platformer3D.DayNight

open System
open System.Numerics
open Mibo

let inline lerpColor (a: Color) (b: Color) (t: float32) =
  let t = Math.Clamp(t, 0.0f, 1.0f)

  Color.create
    (byte(float32 a.R + t * (float32 b.R - float32 a.R)))
    (byte(float32 a.G + t * (float32 b.G - float32 a.G)))
    (byte(float32 a.B + t * (float32 b.B - float32 a.B)))
    255uy

let getSkyColor time : Color =
  if time < 6.0f then
    Color.rgb 10uy 10uy 30uy
  elif time < 8.0f then
    lerpColor
      (Color.rgb 10uy 10uy 30uy)
      (Color.rgb 100uy 149uy 237uy)
      ((time - 6.0f) / 2.0f)
  elif time < 16.0f then
    Color.rgb 100uy 149uy 237uy
  elif time < 18.0f then
    lerpColor
      (Color.rgb 100uy 149uy 237uy)
      (Color.rgb 50uy 50uy 100uy)
      ((time - 16.0f) / 2.0f)
  elif time < 20.0f then
    lerpColor
      (Color.rgb 50uy 50uy 100uy)
      (Color.rgb 10uy 10uy 30uy)
      ((time - 18.0f) / 2.0f)
  else
    Color.rgb 10uy 10uy 30uy

let getAmbientColor time : Color =
  if time < 5.0f || time > 19.0f then
    Color.rgb 40uy 50uy 120uy
  elif time < 7.0f then
    let t = (time - 5.0f) / 2.0f
    let r = byte(int(15.0f + t * 80.0f))
    let g = byte(int(20.0f + t * 100.0f))
    let b = byte(int(45.0f + t * 110.0f))
    Color.rgb r g b
  elif time < 17.0f then
    Color.rgb 95uy 130uy 155uy
  elif time < 19.0f then
    let t = (time - 17.0f) / 2.0f
    let r = byte(int(95.0f + t * 40.0f))
    let g = byte(int(130.0f + t * 50.0f))
    let b = byte(int(155.0f + t * 60.0f))
    Color.rgb r g b
  else
    Color.rgb 40uy 50uy 120uy

let inline getAmbientIntensity time : float32 =
  let color = getAmbientColor time
  let avg = (float32 color.R + float32 color.G + float32 color.B) / 3.0f
  MathF.Max(avg / 255.0f * 0.7f, 0.05f)

// ---------------------------------------------------------------------------
// Single directional light on a ~190° arc.
// ---------------------------------------------------------------------------

[<Literal>]
let private arcDegrees = 190.0f

[<Literal>]
let private fadeDegrees = 10.0f

let private celestialArc (t: float32) (arcRadius: float32) : Vector3 =
  let startAngle = -5.0f * MathF.PI / 180.0f
  let endAngle = 185.0f * MathF.PI / 180.0f
  let angle = startAngle + t * (endAngle - startAngle)

  let pos =
    Vector3(
      MathF.Cos(angle) * arcRadius,
      -MathF.Sin(angle) * arcRadius * 0.6f,
      MathF.Sin(angle * 0.5f) * arcRadius * 0.5f
    )

  Vector3.Normalize(pos)

let getPrimaryLightDirection (time: float32) (arcRadius: float32) : Vector3 =
  if time >= 6.0f && time <= 18.0f then
    celestialArc ((time - 6.0f) / 12.0f) arcRadius
  else
    let t =
      if time > 18.0f then
        (time - 18.0f) / 12.0f
      else
        (time + 6.0f) / 12.0f

    celestialArc t arcRadius

let getPrimaryLightColor(time: float32) : Color =
  if time >= 6.0f && time <= 18.0f then
    if time < 8.0f then
      lerpColor
        (Color.rgb 255uy 150uy 80uy)
        (Color.rgb 255uy 245uy 210uy)
        ((time - 6.0f) / 2.0f)
    elif time < 16.0f then
      Color.rgb 255uy 245uy 210uy
    else
      lerpColor
        (Color.rgb 255uy 245uy 210uy)
        (Color.rgb 255uy 120uy 60uy)
        ((time - 16.0f) / 2.0f)
  else
    Color.rgb 160uy 190uy 230uy

let getPrimaryLightIntensity(time: float32) : float32 =
  if time >= 6.0f && time <= 18.0f then
    let t = (time - 6.0f) / 12.0f

    if t * arcDegrees < fadeDegrees then
      t * arcDegrees / fadeDegrees
    elif (1.0f - t) * arcDegrees < fadeDegrees then
      (1.0f - t) * arcDegrees / fadeDegrees
    else
      1.0f
  else
    let t =
      if time > 18.0f then
        (time - 18.0f) / 12.0f
      else
        (time + 6.0f) / 12.0f

    let maxMoon = 0.3f

    if t * arcDegrees < fadeDegrees then
      t * arcDegrees / fadeDegrees * maxMoon
    elif (1.0f - t) * arcDegrees < fadeDegrees then
      (1.0f - t) * arcDegrees / fadeDegrees * maxMoon
    else
      maxMoon

// -------------------------------------------------------------
// DayNight Sub-system (backend-agnostic)
// -------------------------------------------------------------

module DayNightSystem =
  type DayNightModel() =
    member val TimeOfDay = 12.0f with get, set
    member val DayDuration = 60.0f with get, set
    member val TotalTime = 0.0f with get, set

  let init() = DayNightModel()

  let update dt (model: DayNightModel) =
    model.TimeOfDay <-
      (model.TimeOfDay + dt * (24.0f / model.DayDuration)) % 24.0f

    model.TotalTime <- model.TotalTime + dt
    model
