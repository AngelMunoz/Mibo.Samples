namespace LiveMap.Semantics

open System.Numerics
open Mibo.Layout
open Mibo.Markup

/// The bridge between the two authoring surfaces.
///
/// Flow stamps are F# values: a `Stamp<'T>` paints an area and knows
/// nothing about documents. A document kernel is a per-cell function:
/// `generate forest` calls `int -> int -> 'T` for every cell of the
/// area it covers. This module turns the first into the second, so a
/// `.kdl` or `.xml` file can name a stamp the game wrote.
///
/// A kernel that is a pure function of its coordinates tiles seamlessly
/// over any area. A kernel baked from a stamp repeats the shape, because
/// the stamp is a fixed picture; that is the right trade for a piece
/// with a silhouette (a camp, a courtyard) and the wrong one for a field
/// of trees, which is a pure function instead.
module Bake =

  /// Runs a stamp over a scratch grid of the stamp's own size and reads
  /// the result back as a kernel.
  ///
  /// The kernel receives coordinates local to the generated area, so a
  /// document that states a rectangle larger than the stamp sees the
  /// stamp tile from the rectangle's origin. `empty` fills the cells the
  /// stamp left untouched.
  let kernel
    (width: int)
    (height: int)
    (empty: 'T)
    (stamp: Stamp<'T>)
    : Doc.Kernel<'T> =
    let scratch = CellGrid2D.create width height Vector2.One Vector2.Zero

    let section: GridSection2D<'T> = {
      BackingGrid = scratch
      OffsetX = 0
      OffsetY = 0
      Width = width
      Height = height
    }

    // the ad-hoc paint path: this bake wants the cells, not landmarks
    Flow.paint stamp section |> ignore

    let cells =
      Array.init (width * height) (fun index ->
        match scratch.Cells[index] with
        | ValueSome cell -> cell
        | ValueNone -> empty)

    let wrap (value: int) (size: int) = ((value % size) + size) % size

    Doc.Gen2(fun x y -> cells[wrap y height * width + wrap x width])
