module MonoPlatformer.DayNight

open System
open Microsoft.Xna.Framework
open MonoPlatformer.Constants

// -------------------------------------------------------------
// Day / Night Cycle
// -------------------------------------------------------------
[<Struct>]
type State = {
  TimeOfDay: float32
  DayDuration: float32
}

let initial = {
  TimeOfDay = 12.0f
  DayDuration = 60.0f
}

let inline update dt state =
  let hoursPerSecond = 24.0f / state.DayDuration

  {
    state with
        TimeOfDay = (state.TimeOfDay + dt * hoursPerSecond) % 24.0f
  }

let inline lerpColor (a: Color) (b: Color) (t: float32) =
  let t = Math.Clamp(t, 0.0f, 1.0f)

  Color(
    int(float32 a.R + t * (float32 b.R - float32 a.R)),
    int(float32 a.G + t * (float32 b.G - float32 a.G)),
    int(float32 a.B + t * (float32 b.B - float32 a.B))
  )

let getSkyColors time : Color * Color =
  let midnightTop = Color(10, 10, 30)
  let midnightBot = Color(20, 20, 40)
  let dayTop = Color(100, 149, 237)
  let dayBot = Color(173, 216, 230)
  let sunsetTop = Color(50, 50, 100)
  let sunsetBot = Color(255, 80, 50)

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
    (int top.R + int top.G + int top.B + int bot.R + int bot.G + int bot.B) / 6
    |> float32

  let intensity = MathF.Max(avg / 255.0f, 0.12f)

  Color(
    int(intensity * 255.0f),
    int(intensity * 245.0f),
    int(intensity * 230.0f)
  )

// Sun and moon are mutually exclusive — never active simultaneously.
// This avoids doubling directional-light uniform uploads and SDF
// raymarch cost during dawn/dusk.
let getSunIntensity time : float32 =
  if time < 5.0f || time > 19.0f then 0.0f
  elif time < 7.0f then (time - 5.0f) / 2.0f
  elif time < 17.0f then 1.0f
  else (19.0f - time) / 2.0f

let getMoonIntensity time : float32 =
  if time >= 5.0f && time <= 19.0f then 0.0f else 1.0f

let orbitalPositions (centerX: float32) (state: State) =
  let centerY = groundLevel - 200.0f
  let radiusX = 500.0f
  let radiusY = 200.0f
  let sunAngle = (state.TimeOfDay - 18.0f) / 24.0f * MathF.PI * 2.0f
  let moonAngle = sunAngle + MathF.PI
  let sunX = centerX + radiusX * MathF.Cos(sunAngle)
  let sunY = centerY + radiusY * MathF.Sin(sunAngle)
  let moonX = centerX + radiusX * MathF.Cos(moonAngle)
  let moonY = centerY + radiusY * MathF.Sin(moonAngle)
  Vector2(sunX, sunY), Vector2(moonX, moonY)
