namespace LiveMap

open LiveMap.Catalog
open Mibo.Layout

/// One square cell of a flat map: the atlas tile that paints it, and
/// whether it blocks movement. Every document word resolves to one of
/// these, so the map carries meaning as well as paint.
[<Struct>]
type Cell = {
  Tile: TileInfo
  /// True when the cell stops movement. The hover panel reports it, and
  /// a game would query it for collision.
  Solid: bool
}

/// One cell of a block map: the model that paints it, how many cells
/// tall it stands, where its base sits, how many cells its instance covers,
/// and whether it blocks movement.
///
/// `Height` is in cells. A word that means "the model as authored"
/// states the model's own mesh height, so nothing stretches; a word that
/// means "a wall" states a taller number and the model stretches on Y.
///
/// `Lift` is in cells too, and a document never states it: the ground
/// layer stands on the plane, and the build lifts every layer above it by
/// the height the layers below reach at that cell, so a decoration stands
/// on the ground instead of replacing it.
///
/// `Span` is the grid-plane footprint of the instance. `One` means the
/// column covers its own cell; a plate covers a rectangle, and one model
/// stretches over it.
[<Struct>]
type BlockCell = {
  Model: ModelInfo
  Height: float32
  Lift: float32
  Span: InstanceSpan
  Solid: bool
}

module BlockCell =

  /// A block as the model was authored: the column is as tall as the
  /// mesh, so nothing stretches, and it covers its own cell.
  let ofModel (model: ModelInfo) (solid: bool) : BlockCell = {
    Model = model
    Height = model.SizeY
    Lift = 0f
    Span = One
    Solid = solid
  }

  /// A column raised to a stated height in cells — a wall, a pillar, a
  /// hill's flank. The model stretches on Y.
  let column (model: ModelInfo) (height: float32) (solid: bool) : BlockCell = {
    Model = model
    Height = height
    Lift = 0f
    Span = One
    Solid = solid
  }

/// The two maps LiveMap shows.
type Mode =
  | Flat
  | Blocks

/// The two document syntaxes. Every map exists in both files, and the
/// two must build the same grid.
type Syntax =
  | Kdl
  | Xml

module Mode =

  let all: Mode list = [ Flat; Blocks ]

  let label =
    function
    | Flat -> "square 2D"
    | Blocks -> "blocks 3D"

  /// The document's base name: a mode and a syntax name one file.
  let fileName =
    function
    | Flat -> "square-2d"
    | Blocks -> "blocks-3d"

module Syntax =

  let all: Syntax list = [ Kdl; Xml ]

  let label =
    function
    | Kdl -> "KDL"
    | Xml -> "XML"

  let extension =
    function
    | Kdl -> "kdl"
    | Xml -> "xml"
