namespace Platformer

open System
open System.Numerics
open Mibo
open Platformer.Constants

module DayNight =

  [<Struct>]
  type State = {
    TimeOfDay: float32
    DayDuration: float32
  }

  let initial = {
    TimeOfDay = 12.0f
    DayDuration = 60.0f
  }

  let inline update dt (state: State) = {
    state with
        TimeOfDay = (state.TimeOfDay + dt * (24.0f / state.DayDuration)) % 24.0f
  }

  let inline lerpColor (a: Color) (b: Color) (t: float32) =
    let t = Math.Clamp(t, 0.0f, 1.0f)

    Color.create
      (byte(float32 a.R + t * (float32 b.R - float32 a.R)))
      (byte(float32 a.G + t * (float32 b.G - float32 a.G)))
      (byte(float32 a.B + t * (float32 b.B - float32 a.B)))
      255uy

  let getSkyColors time : Color * Color =
    let midnightTop = Color.rgb 10uy 10uy 30uy
    let midnightBot = Color.rgb 20uy 20uy 40uy
    let dayTop = Color.rgb 100uy 149uy 237uy
    let dayBot = Color.rgb 173uy 216uy 230uy
    let sunsetTop = Color.rgb 50uy 50uy 100uy
    let sunsetBot = Color.rgb 255uy 80uy 50uy

    if time < 6.0f then
      midnightTop, midnightBot
    elif time < 8.0f then
      let t = (time - 6.0f) / 2.0f
      lerpColor midnightTop dayTop t, lerpColor midnightBot dayBot t
    elif time < 16.0f then
      dayTop, dayBot
    elif time < 18.0f then
      let t = (time - 16.0f) / 2.0f
      lerpColor dayTop sunsetTop t, lerpColor dayBot sunsetBot t
    elif time < 20.0f then
      let t = (time - 18.0f) / 2.0f
      lerpColor sunsetTop midnightTop t, lerpColor sunsetBot midnightBot t
    else
      midnightTop, midnightBot

  let getAmbientColor time : Color =
    let top, bot = getSkyColors time

    let avg =
      float32(
        int top.R + int top.G + int top.B + int bot.R + int bot.G + int bot.B
      )
      / 6.0f

    let intensity = MathF.Max(avg / 255.0f, 0.12f)

    Color.create
      (byte(intensity * 255.0f))
      (byte(intensity * 245.0f))
      (byte(intensity * 230.0f))
      255uy

  let getSunIntensity time : float32 =
    if time < 5.0f || time > 19.0f then 0.0f
    elif time < 7.0f then (time - 5.0f) / 2.0f
    elif time < 17.0f then 1.0f
    else (19.0f - time) / 2.0f

  let inline getMoonIntensity time : float32 =
    if time >= 5.0f && time <= 19.0f then 0.0f else 1.0f

  let orbitalPositions (centerX: float32) (state: State) =
    let centerY = groundLevel - 200.0f
    let sunAngle = (state.TimeOfDay - 18.0f) / 24.0f * MathF.PI * 2.0f
    let moonAngle = sunAngle + MathF.PI

    Vector2(
      centerX + 500.0f * MathF.Cos sunAngle,
      centerY + 200.0f * MathF.Sin sunAngle
    ),
    Vector2(
      centerX + 500.0f * MathF.Cos moonAngle,
      centerY + 200.0f * MathF.Sin moonAngle
    )

// -------------------------------------------------------------
// DayNight Sub-system (M_U)
// -------------------------------------------------------------

module DayNightSystem =
  [<Struct>]
  type DayNightModel = {
    Time: DayNight.State
    TotalTime: float32
  }

  let init() = {
    Time = DayNight.initial
    TotalTime = 0.0f
  }

  let update dt (model: DayNightModel) = {
    Time = DayNight.update dt model.Time
    TotalTime = model.TotalTime + dt
  }
