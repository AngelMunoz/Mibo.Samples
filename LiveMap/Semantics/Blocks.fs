module LiveMap.Semantics.Blocks

open LiveMap
open LiveMap.Catalog
open Mibo.Layout

/// The model a word paints. The catalog is generated from the models on
/// disk, so a name that is not in it is a mistake in this file: it fails
/// at startup with the name in the message.
let private model(name: string) : ModelInfo =
  match Models.tryByName name with
  | ValueSome info -> info
  | ValueNone -> failwith $"the model catalog has no '{name}'"

// ── terrain ──────────────────────────────────────────────────
// Squares that stack into ground. The low and edge pieces give a
// surface relief without a height change.

let grass = BlockCell.ofModel (model "block-grass") true
let grassLow = BlockCell.ofModel (model "block-grass-low") true
let grassEdge = BlockCell.ofModel (model "block-grass-edge") true
let grassCorner = BlockCell.ofModel (model "block-grass-corner") true
let grassNarrow = BlockCell.ofModel (model "block-grass-narrow") true
let snow = BlockCell.ofModel (model "block-snow") true
let snowLow = BlockCell.ofModel (model "block-snow-low") true

// ── raised ground ────────────────────────────────────────────
// One model stretched on Y. A document that fills with a wall gets a
// three-cell-high rampart from one word.

let wall = BlockCell.column (model "block-grass") 2.5f true
let pillar = BlockCell.column (model "block-grass") 4.0f true
let kerb = BlockCell.column (model "block-grass-narrow") 0.75f true

// ── ground pieces ────────────────────────────────────────────
// The kit authors the ground in pieces of one, two, and four cells, and
// each piece carries its own bevel at its border. A word states the size of
// the piece it names, so a document places one instance per piece at the
// scale the piece was authored for: `set 0 0 grassLarge` is four cells of
// ground drawn with one model, with no stretched bevel around it.

let private piece
  (name: string)
  (across: int)
  (deep: int)
  (solid: bool)
  : BlockCell =
  {
    BlockCell.ofModel (model name) solid with
        Span = Span(across, deep)
  }

/// Two cells by two, a cell tall.
let grassLarge = piece "block-grass-large" 2 2 true

/// Two cells by two, half a cell tall: a field a step below the rest.
let grassLowLarge = piece "block-grass-low-large" 2 2 true

/// Two cells by two, two cells tall: a plateau a step above the rest.
let grassTall = piece "block-grass-large-tall" 2 2 true

/// Two cells by one, for the rows a square piece does not fit.
let grassLong = piece "block-grass-long" 2 1 true
let grassLowLong = piece "block-grass-low-long" 2 1 true
let snowLarge = piece "block-snow-large" 2 2 true
let snowLowLarge = piece "block-snow-low-large" 2 2 true
let snowLong = piece "block-snow-long" 2 1 true
let snowEdge = BlockCell.ofModel (model "block-snow-edge") true
let snowCorner = BlockCell.ofModel (model "block-snow-corner") true

// ── planted things ───────────────────────────────────────────

let tree = BlockCell.ofModel (model "tree") true
let pine = BlockCell.ofModel (model "tree-pine") true
let pineSmall = BlockCell.ofModel (model "tree-pine-small") true
let snowTree = BlockCell.ofModel (model "tree-snow") true
let hedge = BlockCell.ofModel (model "hedge") true
let stones = BlockCell.ofModel (model "stones") false

// ── built things ─────────────────────────────────────────────

let crate = BlockCell.ofModel (model "crate") true
let crateStrong = BlockCell.ofModel (model "crate-strong") true
let barrel = BlockCell.ofModel (model "barrel") true
let fence = BlockCell.ofModel (model "fence-straight") true
let pipe = BlockCell.ofModel (model "pipe") true
let ladder = BlockCell.ofModel (model "ladder") false
let platform = BlockCell.ofModel (model "platform") false
let sign = BlockCell.ofModel (model "sign") false
let flag = BlockCell.ofModel (model "flag") false
let spring = BlockCell.ofModel (model "spring") false
let spike = BlockCell.ofModel (model "spike-block") true
let coin = BlockCell.ofModel (model "coin-gold") false

/// A plate: one instance stretched over the cells it covers. A word states
/// the model; the document states the size with `spanX=` and `spanZ=`, so
/// one slab serves a whole ground plate and a small deck alike.
let slab = BlockCell.ofModel (model "platform") false

/// The words a block map document may use: `fill grass`, `set 2 3 wall`.
/// The same table rule holds as in the flat palette — this is the whole
/// vocabulary, and a word outside it fails the build.
let words: (string * BlockCell)[] = [|
  "grass", grass
  "grassLow", grassLow
  "grassEdge", grassEdge
  "grassCorner", grassCorner
  "grassNarrow", grassNarrow
  "snow", snow
  "snowLow", snowLow
  "wall", wall
  "pillar", pillar
  "kerb", kerb
  "grassLarge", grassLarge
  "grassLowLarge", grassLowLarge
  "grassTall", grassTall
  "grassLong", grassLong
  "grassLowLong", grassLowLong
  "snowLarge", snowLarge
  "snowLowLarge", snowLowLarge
  "snowLong", snowLong
  "snowEdge", snowEdge
  "snowCorner", snowCorner
  "tree", tree
  "pine", pine
  "pineSmall", pineSmall
  "snowTree", snowTree
  "hedge", hedge
  "stones", stones
  "crate", crate
  "crateStrong", crateStrong
  "barrel", barrel
  "fence", fence
  "pipe", pipe
  "ladder", ladder
  "platform", platform
  "sign", sign
  "flag", flag
  "spring", spring
  "spike", spike
  "coin", coin
  "slab", slab
|]
