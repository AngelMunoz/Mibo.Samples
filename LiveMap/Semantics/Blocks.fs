module LiveMap.Semantics.Blocks

open LiveMap
open LiveMap.Catalog

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
|]
