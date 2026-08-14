namespace PrimitiveGallery.MonoGame

open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────
// Types — view-edge types for the MonoGame clients. Mirrors
// Defli3D/MonoDX12/Types.fs in role: render layers, XNB asset
// paths, and the lazily-built unit-primitive cache (the
// GraphicsDevice only exists after the game initializes).
// ─────────────────────────────────────────────────────────────

module Layers =

  /// The 2D pass's render layers (the 3D pass has no layers). The
  /// invariant: every TEXT draw uses Labels — the top layer — so text
  /// always renders in front of the shapes (0), panels (-1), and the
  /// split backdrop (-2).
  [<Literal>]
  let Backdrop = -2<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Panel = -1<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Labels = 1000<Mibo.Elmish.Graphics2D.RenderLayer>

module Paths =

  /// XNB asset name for the HUD font (matches the .mgcb /build name).
  [<Literal>]
  let Font = "Fonts/Monogram"

module Prims =

  /// The six unit primitives, built once against the GraphicsDevice
  /// on first use (the device only exists after the host initializes).
  let mutable private setV: Primitive3D.PrimitiveSet voption = ValueNone

  let get(gd: GraphicsDevice) : Primitive3D.PrimitiveSet =
    match setV with
    | ValueSome s -> s
    | ValueNone ->
      let s = Primitive3D.create gd
      setV <- ValueSome s
      s
