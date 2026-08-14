namespace PrimitiveGallery

open Mibo.Adaptive

/// The composition root. The screen selector is the shell state; elapsed
/// time (seconds, accumulated by Update) drives the 3D inspection rotation.
/// Everything else (the layouts, the HUD) is static data.
type State = {
  Screen: cval<Screen>
  Elapsed: cval<float32>
}

/// The state cell — the frame force and the input subscriptions read the
/// CURRENT state through this holder, mirroring the Defli3D StateCell idiom.
[<Sealed>]
type StateCell(value: State) =
  member val Value = value with get, set

module State =

  let init() : State = {
    Screen = CVal.create Screen.Shapes2D
    Elapsed = CVal.create 0f
  }
