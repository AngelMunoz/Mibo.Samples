module LiveMap.Document

open System.Collections.Immutable
open System.IO
open System.Numerics
open LiveMap
open LiveMap.Semantics
open Mibo.Layout
open Mibo.Markup

/// A map that built: the document's layers, bottom first, each with its
/// own grid and landmarks.
///
/// The two modes hold different cell types, so the map is a case. The
/// blocks mode carries one more thing: the grids the view draws, one per
/// layer, with every layer above the ground lifted on top of the ones
/// below it (see `lift`).
type Map =
  | FlatMap of layers: DocFlow.BuiltLayer<Cell>[]
  | BlockMap of
    layers: DocFlow.BuiltLayer<BlockCell>[] *
    drawn: CellGrid2D<BlockCell>[]

module Map =

  /// The map's size in cells. Every layer spans the whole map, and a build
  /// always returns at least one layer, so the bottom layer speaks for all
  /// of them.
  let width =
    function
    | FlatMap layers -> layers[0].Grid.Width
    | BlockMap(layers, _) -> layers[0].Grid.Width

  let height =
    function
    | FlatMap layers -> layers[0].Grid.Height
    | BlockMap(layers, _) -> layers[0].Grid.Height

  /// How many layers the document resolved to, as the status line reads
  /// it. `main` is one of them, and a build always returns at least one.
  let layerCountWords(map: Map) : string =
    let count =
      match map with
      | FlatMap layers -> layers.Length
      | BlockMap(layers, _) -> layers.Length

    if count = 1 then "1 layer" else $"{count} layers"

/// What LiveMap shows: which map, in which syntax, from which file, and
/// the outcome of the last build.
type Model = {
  Mode: Mode
  Syntax: Syntax
  /// The directory the documents live in. A mode and a syntax name one
  /// file inside it.
  Directory: string
  Path: string
  /// The last map that built. It stays on screen while a broken document
  /// is being fixed.
  Map: Map voption
  /// One line for the HUD: the build result, or the positioned error a
  /// broken document raised.
  Status: string
}

[<Struct>]
type Msg =
  | SelectMode of mode: Mode
  | SelectSyntax of syntax: Syntax
  | SelectPath of path: string
  | Loaded of source: string
  | ReadFailed of reason: string

/// What an update produced, for the router to translate. A build that
/// landed re-frames the view; a failure only moves the status line.
[<Struct>]
type Event =
  | Built
  | BuildFailed

/// The file a mode and a syntax name, under the maps directory.
let path (directory: string) (mode: Mode) (syntax: Syntax) : string =
  Path.Combine(directory, $"{Mode.fileName mode}.{Syntax.extension syntax}")

/// One grid per layer, ready to draw: every layer above the ground is
/// lifted by the height the layers below it reach at that cell, so a
/// decoration stands on the ground rather than replacing it.
///
/// A block cell holds one column, and the view draws one instance per
/// populated cell. Drawn as they are, two layers that both painted a cell
/// would put two columns in the same place, and the shorter one would be
/// buried inside the taller one — a hut's platform floor is 0.195 cells
/// tall and the field's grass block is 1 cell, so the hut would sink into
/// the terrain. The lift hands the upper layer the ground's depth to stand
/// on, and it leaves the terrain whole underneath.
let private lift
  (layers: DocFlow.BuiltLayer<BlockCell>[])
  : CellGrid2D<BlockCell>[] =
  let bottom = layers[0].Grid
  // the top of the stack so far, per cell, in cells above the plane
  let tops = Array.zeroCreate(bottom.Width * bottom.Height)

  layers
  |> Array.map(fun layer ->
    let drawn =
      CellGrid2D.create
        bottom.Width
        bottom.Height
        (Vector2(Constants.cellSize, Constants.cellSize))
        Vector2.Zero

    CellGrid2D.iter
      (fun x y cell ->
        let index = x + y * bottom.Width
        let foot = tops[index]

        CellGrid2D.set x y { cell with Lift = foot } drawn
        tops[index] <- foot + cell.Height)
      layer.Grid

    drawn)

/// Parses, resolves, emits, and lays every layer of a document out,
/// keeping the grid and the landmarks of each one.
///
/// `DocFlow.buildLayers` runs the same steps and builds every grid at one
/// cell per tile. LiveMap owns its grids because a cell has to be 48
/// pixels in the flat mode, so it emits the layers itself and paints them
/// with `Flow.runLayers` — the plural of the `Flow.run` a single-grid
/// build uses.
let private buildDocument
  (surface: Doc.Surface<'T>)
  (parse: string -> Result<ImmutableArray<Node>, string>)
  (grid: int -> int -> CellGrid2D<'T>)
  (wrap: DocFlow.BuiltLayer<'T>[] -> Map)
  (source: string)
  : Result<Map, string> =
  let paint (roots: ImmutableArray<Node>) (root: Doc.Item<'T>) =
    Doc.findMapNode roots
    |> ValueOption.map(fun node ->
      Doc.dimsOf(source, node)
      |> Result.map(fun dims ->
        // one stamp per layer, one grid per stamp: `main` is layer 0 when
        // the map paints anything of its own, and every stated layer
        // follows in document order
        let emitted = DocFlow.emitLayers root

        let stamps = emitted |> Array.map(fun struct (_, stamp) -> stamp)

        let grids = Array.init stamps.Length (fun _ -> grid dims.W dims.H)

        let painted = Flow.runLayers stamps grids

        let layers: DocFlow.BuiltLayer<'T>[] =
          Array.init painted.Length (fun i ->
            let struct (name, _) = emitted[i]
            let struct (built, marks) = painted[i]

            {
              Name = name
              Grid = built
              Landmarks = marks
            })

        wrap layers))
    |> ValueOption.defaultValue(
      Error "the document needs a map node with two dimensions: map 36 20"
    )

  parse source
  |> Result.bind(fun roots ->
    Doc.resolve surface source roots
    |> Result.bind (function
      | [| root |] -> paint roots root
      | many -> Error $"the document resolved to {many.Length} roots, not one"))

/// Builds one document. A pure function of its three inputs: the same
/// document and the same mode always produce the same map, so the two
/// syntaxes of a file can be compared cell for cell.
let build (mode: Mode) (syntax: Syntax) (source: string) : Result<Map, string> =
  let parse =
    match syntax with
    | Kdl -> Kdl.parse
    | Xml -> Xml.parse

  match mode with
  | Flat ->
    buildDocument
      Flat.surface
      parse
      (fun w h ->
        CellGrid2D.create
          w
          h
          (Vector2(Constants.cellPixels, Constants.cellPixels))
          Vector2.Zero)
      FlatMap
      source
  | Blocks ->
    let size = Vector2(Constants.cellSize, Constants.cellSize)

    buildDocument
      Volume.surface
      parse
      (fun w h -> CellGrid2D.create w h size Vector2.Zero)
      (fun layers -> BlockMap(layers, lift layers))
      source

let init(directory: string) : Model = {
  Mode = Flat
  Syntax = Kdl
  Directory = directory
  Path = path directory Flat Kdl
  Map = ValueNone
  Status = "waiting for the document"
}

/// Applies one message. A valid build replaces the map; an invalid one
/// keeps the last good map and puts the positioned error on the status
/// line, so a broken document never half-paints the view.
let update (msg: Msg) (model: Model) : struct (Model * Event voption) =
  match msg with
  // A mode or a syntax change names another file, so the map on screen
  // belongs to a document that is no longer loaded: it goes, and the
  // watcher's first read puts the new one up.
  | SelectMode mode ->
    struct ({
              model with
                  Mode = mode
                  Path = path model.Directory mode model.Syntax
                  Map = ValueNone
                  Status = "loading the document"
            },
            ValueNone)
  | SelectSyntax syntax ->
    struct ({
              model with
                  Syntax = syntax
                  Path = path model.Directory model.Mode syntax
                  Map = ValueNone
                  Status = "loading the document"
            },
            ValueNone)
  | SelectPath path -> struct ({ model with Path = path }, ValueNone)
  | Loaded source ->
    match build model.Mode model.Syntax source with
    | Ok map ->
      let status =
        $"built {Map.width map}x{Map.height map} · {Map.layerCountWords map}"

      struct ({
                model with
                    Map = ValueSome map
                    Status = status
              },
              ValueSome Built)
    | Error reason ->
      struct ({ model with Status = reason }, ValueSome BuildFailed)
  | ReadFailed reason ->
    struct ({
              model with
                  Status = $"read failed: {reason}"
            },
            ValueSome BuildFailed)
