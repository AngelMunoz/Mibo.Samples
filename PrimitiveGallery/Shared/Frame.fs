namespace PrimitiveGallery

open Mibo.Adaptive

module Frame =

  /// Everything the renderer needs, resolved and packed once per Step.
  [<Struct>]
  type RenderFrame = {
    Screen: Screen
    /// Elapsed seconds since boot — drives the 3D bodies' rotation.
    Elapsed: float32
  }

  /// Forcing the frame: resolve the screen projection once and pack the struct.
  let inline force(getState: unit -> State) : unit -> RenderFrame =
    fun () ->
      let state = getState()

      {
        Screen = state.Screen |> AVal.getValue
        Elapsed = state.Elapsed |> AVal.getValue
      }
