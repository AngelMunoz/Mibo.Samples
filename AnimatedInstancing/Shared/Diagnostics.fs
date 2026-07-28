namespace AnimatedInstancing

/// EMA-smoothed FPS / frame-time counter. Sampled wall-clock once per Draw
/// (in the HUD view), not in Update — measuring in Update hides frame drops.
module Diagnostics =
  open System

  type DiagnosticsModel() =
    let sw = System.Diagnostics.Stopwatch.StartNew()

    /// EMA-smoothed FPS so the counter doesn't jitter by ±1 every frame.
    member val Fps = 0 with get, set

    /// Last frame's wall-clock interval in milliseconds (0.0 until first tick).
    member val FrameTime = 0.0f with get, set

    /// Sample the wall-clock interval since the last call. Call once per Draw.
    member this.Tick() =
      let elapsed = float32 sw.Elapsed.TotalSeconds
      sw.Restart()

      this.FrameTime <- elapsed * 1000.0f

      if elapsed > 0.0f then
        let instant = 1.0f / elapsed

        this.Fps <-
          if this.Fps = 0 then
            int instant
          else
            int(MathF.Round(float32 this.Fps * 0.9f + instant * 0.1f))

  let init() = DiagnosticsModel()
