module LiveMap.Check

open System.IO
open LiveMap
open LiveMap.Semantics
open Mibo.Layout
open Mibo.Markup

/// One cell of one map: the layer whose paint should answer for it, and the
/// region of the document that should cover it. An empty region means the
/// cell belongs to no element: ground a kernel painted, or a blank cell a
/// scatter left between its children.
let private probes: (Mode * int * int * string * string)[] = [|
  // the routes belong to the ground layer: no prop may cover them
  Flat, 20, 11, "ground", ""
  Flat, 12, 7, "ground", ""
  // the woods, the buildings, and the thicket stand in the decor layer
  Flat, 1, 1, "decor", ""
  Flat, 10, 17, "decor", "court"
  Flat, 19, 16, "decor", "camp"
  // one cell below the plaza plot, so the yard is the smallest region
  Flat, 31, 18, "decor", "yard"
  Flat, 15, 4, "decor", "thicket"
  // blocks 3D: a piece of each material, and what stands on them. The ground
  // answers with the piece's own name, because each piece is a node
  Blocks, 2, 2, "ground", "grassLowLarge"
  Blocks, 5, 13, "ground", "grassLarge"
  Blocks, 14, 4, "ground", "grassTall"
  Blocks, 30, 13, "ground", "snowLarge"
  Blocks, 28, 10, "decor", "snowTree"
  Blocks, 14, 13, "decor", "plot"
  Blocks, 14, 6, "decor", "plot"
  Blocks, 5, 10, "decor", "stones"
|]

let private read(path: string) : Result<string, string> =
  try
    Ok(File.ReadAllText path)
  with error ->
    Error $"cannot read the file: {error.Message}"

/// The cells that differ between two grids, or -1 when the grids are not
/// the same shape at all.
let private countDifferences<'T when 'T: equality>
  (left: CellGrid2D<'T>)
  (right: CellGrid2D<'T>)
  : int =
  if left.Width <> right.Width || left.Height <> right.Height then
    -1
  else
    let mutable differences = 0

    for y in 0 .. left.Height - 1 do
      for x in 0 .. left.Width - 1 do
        if CellGrid2D.get x y left <> CellGrid2D.get x y right then
          differences <- differences + 1

    differences

/// The layers of two builds of the same document: the same names in the
/// same order, and the same cells in each layer. `-1` when the two builds
/// do not even hold the same number of layers.
let private compareLayers<'T when 'T: equality>
  (left: DocFlow.BuiltLayer<'T>[])
  (right: DocFlow.BuiltLayer<'T>[])
  : int =
  if left.Length <> right.Length then
    -1
  else
    let mutable differences = 0
    let mutable i = 0

    while differences = 0 && i < left.Length do
      if left[i].Name <> right[i].Name then
        differences <- -1
      else
        differences <- countDifferences left[i].Grid right[i].Grid

      i <- i + 1

    differences

/// Two builds of one document must answer the same occupancy: the same
/// instances, the same rectangles, and the same number of hidden cells.
let private sameOccupancy<'T>
  (left: DocFlow.BuiltLayer<'T>[])
  (right: DocFlow.BuiltLayer<'T>[])
  : bool =
  left.Length = right.Length
  && Array.forall2
    (fun (a: DocFlow.BuiltLayer<'T>) (b: DocFlow.BuiltLayer<'T>) ->
      a.Occupancy.Cells.Length = b.Occupancy.Cells.Length
      && a.Occupancy.Claimed = b.Occupancy.Claimed)
    left
    right

/// The hover's own query: the topmost layer that painted a cell, and the
/// region that layer reports for it. Both answers read the same
/// `ValueOption`, so they compose instead of nesting.
let private probeCell<'T>
  (label: string)
  (layers: DocFlow.BuiltLayer<'T>[])
  (x: int)
  (y: int)
  (expectedLayer: string)
  (expectedRegion: string)
  : int =
  let found = Hover.tryCellAt layers x y

  let layer =
    found
    |> ValueOption.map(fun struct (layer, _) -> layer.Name)
    |> ValueOption.defaultValue ""

  let region =
    found
    |> ValueOption.bind(fun struct (layer, _) ->
      Hover.regionAt x y layer.Landmarks)
    |> ValueOption.map(fun struct (name, _) -> name)
    |> ValueOption.defaultValue ""

  if layer = expectedLayer && region = expectedRegion then
    let shown = if expectedRegion = "" then "(none)" else expectedRegion

    printfn
      "  ok    %-9s      cell %2d,%-2d is in layer %s, region %s"
      label
      x
      y
      layer
      shown

    0
  else
    printfn
      "  FAIL  %-9s      cell %2d,%-2d: expected layer %s and region '%s', found layer %s and region '%s'"
      label
      x
      y
      expectedLayer
      expectedRegion
      layer
      region

    1

/// Builds one mode's two documents with the framework's plural builder,
/// compares the layers, and runs the probes. Returns the failure count.
let private checkMode<'T when 'T: equality>
  (mode: Mode)
  (directory: string)
  (surface: Doc.Surface<'T>)
  : int =
  let label = Mode.fileName mode
  let mutable failures = 0

  // the document's syntax selects the parser, so reading the file and
  // building it are two binds, not two matches
  let build(syntax: Syntax) : Result<DocFlow.BuiltLayer<'T>[], string> =
    let parse(source: string) =
      match syntax with
      | Kdl -> DocFlow.buildLayers(surface, source)
      | Xml -> DocFlow.buildLayersXml(surface, source)

    read(Document.path directory mode syntax) |> Result.bind parse

  let kdl = build Kdl
  let xml = build Xml

  for syntax, result in [ Kdl, kdl; Xml, xml ] do
    match result with
    | Ok layers ->
      // the painted-cell count is what makes a layer a layer; the covered
      // count is the cells the layer's instances stand for, which a plate
      // stretched over a rectangle reports as many while it holds one cell
      let painted(grid: CellGrid2D<'T>) =
        let mutable count = 0
        CellGrid2D.iter (fun _ _ _ -> count <- count + 1) grid
        count

      let covered(occupancy: Occupancy) =
        occupancy.Rects |> Array.sumBy(fun rect -> rect.W * rect.H)

      let names =
        layers
        |> Array.map(fun l ->
          $"{l.Name} ({l.Occupancy.Cells.Length} instances over {covered l.Occupancy} cells of {painted l.Grid} painted, {l.Occupancy.Claimed} covered)")
        |> String.concat ", "

      printfn
        "  ok    %-9s %-3s  %3dx%-3d  %d layer(s): %s"
        label
        (Syntax.label syntax)
        layers[0].Grid.Width
        layers[0].Grid.Height
        layers.Length
        names

      printfn "         %s" (Document.path directory mode syntax)
    | Error reason ->
      failures <- failures + 1
      printfn "  FAIL  %-9s %-3s  %s" label (Syntax.label syntax) reason

  match kdl, xml with
  | Ok kdlLayers, Ok xmlLayers ->
    let differences = compareLayers kdlLayers xmlLayers

    if differences = 0 then
      printfn
        "  ok    %-9s      both syntaxes build the same layers, cell for cell"
        label
    else
      failures <- failures + 1

      printfn
        "  FAIL  %-9s      the two syntaxes differ in %d cells of their layers"
        label
        differences

    if sameOccupancy kdlLayers xmlLayers then
      printfn
        "  ok    %-9s      both syntaxes agree on the instances and the covered cells"
        label
    else
      failures <- failures + 1

      printfn
        "  FAIL  %-9s      the two syntaxes disagree on the occupancy of a layer"
        label

    for probeMode, x, y, expectedLayer, expectedRegion in probes do
      if probeMode = mode then
        failures <-
          failures + probeCell label kdlLayers x y expectedLayer expectedRegion
  | _ -> ()

  printfn ""
  failures

/// The spans: the ground is nine instances over the whole map, the words
/// decide how high each region stands, and the decorations stand on the
/// region they were placed in. It pins the numbers the sample's own
/// document states, so a broken span build cannot pass quietly.
let private checkSpans(directory: string) : int =
  let label = "blocks-3d"
  let syntax = Syntax.Kdl
  let path = Document.path directory Mode.Blocks syntax

  let fail(reason: string) =
    printfn "  FAIL  %-9s      %s" label reason
    1

  let covered(occupancy: Occupancy) =
    occupancy.Rects |> Array.sumBy(fun rect -> rect.W * rect.H)

  match read path |> Result.bind(Document.build Mode.Blocks syntax) with
  | Error reason -> fail reason
  | Ok(Document.FlatMap _) -> fail "the blocks document built as a flat map"
  | Ok(Document.BlockMap(layers, drawn)) ->
    let layerAt name =
      layers |> Array.tryFindIndex(fun layer -> layer.Name = name)

    match layerAt "ground", layerAt "decor" with
    | Some ground, Some decor ->
      let occupancy = layers[ground].Occupancy

      // the pieces: every instance of the ground covers more than one cell
      let pieces =
        occupancy.Rects |> Array.filter(fun rect -> rect.W > 1 || rect.H > 1)

      let groundHeight x y =
        Occupancy.owner x y occupancy
        |> ValueOption.bind(fun at ->
          CellGrid2D.get at.X at.Y layers[ground].Grid)
        |> ValueOption.map(fun cell -> cell.Height)

      // what stands on the terrace is lifted by the terrace, not by the
      // field under it
      let decorLift x y =
        CellGrid2D.get x y drawn[decor]
        |> ValueOption.map(fun cell -> cell.Lift)

      let expected name =
        Blocks.words
        |> Array.tryFind(fun (word, _) -> word = name)
        |> ValueOption.ofOption
        |> ValueOption.map(fun (_, cell) -> cell.Height)

      let report = [
        "every ground instance is a piece of several cells", pieces.Length = 160
        "the pieces cover every cell of the map", covered occupancy = 640
        "the ground paints no cell twice", occupancy.Claimed = 0
        "the north floor is half a cell of grassLowLarge",
        groundHeight 2 2 = expected "grassLowLarge"
        "the west field is a cell of grassLarge",
        groundHeight 2 10 = expected "grassLarge"
        "the terrace is a two-cell piece",
        groundHeight 14 6 = expected "grassTall"
        "the village stands on the terrace",
        decorLift 14 6 = expected "grassTall"
        "a stone in the west field stands on the field",
        decorLift 5 10 = expected "grassLarge"
      ]

      let broken =
        report |> List.filter(fun (_, held) -> not held) |> List.map fst

      match broken with
      | [] ->
        printfn
          "  ok    %-9s      the ground is %d pieces over %d cells at three heights, the decorations lifted onto them"
          label
          pieces.Length
          (covered occupancy)

        0
      | _ ->
        for reason in broken do
          printfn "  FAIL  %-9s      %s" label reason

        broken.Length
    | _ -> fail "the document has no 'ground' and 'decor' layer"

/// Builds every shipped document in both syntaxes and reports what
/// happened.
///
/// A map that fails to build is a broken sample, and a pair whose two
/// syntaxes disagree is a broken front-end. Both are cheap to catch
/// without opening a window, which is what `--check` is for: the same
/// documents the running app loads, built through `DocFlow.buildLayers` —
/// the plural of the single-grid `DocFlow.build`.
///
/// The probes are the hover's own query: a cell, the layer that should
/// answer for it, and the region that should cover it. They fail if the
/// layer stamps or the element rectangles the build reports stop reaching
/// the pointer.
let run(directory: string) : int =
  printfn "LiveMap documents in %s" directory
  printfn ""

  let failures =
    checkMode Flat directory Flat.surface
    + checkMode Blocks directory Volume.surface
    + checkSpans directory

  if failures = 0 then
    printfn
      "every document built, both syntaxes agree layer by layer, and every probe found its layer and region"

    0
  else
    printfn "%d check(s) failed" failures
    1
