// ─────────────────────────────────────────────────────────────
// Bakes a Starling/Sparrow atlas XML into an F# tile catalog
// (compile-time dataset — no runtime XML parsing).
//
// Usage:
//   dotnet fsi tools/gen-tiles.fsx --xml <atlas.xml> --out <file.fs> \
//       --namespace <ns> [options]
//
// Options:
//   --xml <path>         the atlas XML to bake                     (required)
//   --out <path>         the F# file to write                      (required)
//   --namespace <name>   the generated file's namespace            (required)
//   --module <name>      the module inside that namespace          (default: Tiles)
//   --sheet-path <path>  the PNG path the game loads (the SheetPath
//                        literal); defaults to the XML's own name
//                        with a .png extension
//   --tile-size          emit [<Literal>] let TileSize, the first
//                        tile's width — the cell size of a uniform grid
//   --curation <file>    the semantically named tiles and groups (see
//                        the format below); without it the file holds
//                        the raw atlas only
//   --dict-helper <fn>   emit tryByName as <fn> name byName instead of
//                        the self-contained lookup
//   --source <text>      an extra "// Sources:" header line
//   --preset <name>      fill every unstated option from a preset;
//                        the only preset is defli
//   --help
//
// Curation format:
//   binding grassFullA = grass_full_a
//   group groundGrass = grassFullA, grassFullB, grassFullC
//   # comments and blank lines are free
//
// The generated file needs a TileInfo record in the target namespace:
//   type TileInfo = { Name: string; X: int; Y: int; Width: int; Height: int }
//
// The generated file is committed; regenerate when a sheet changes.
// ─────────────────────────────────────────────────────────────
open System
open System.Collections.Generic
open System.IO
open System.Xml.Linq

// ── options ──────────────────────────────────────────────────

/// One baked atlas entry, mirrored from the generated file's record.
type Tile = {
  Name: string
  X: int
  Y: int
  W: int
  H: int
}

/// The curated half of a catalog: the names a game reads and the groups
/// it iterates. Data, not code — it lives in a text file next to the
/// generated catalog.
type Curation = {
  Named: (string * string)[]
  Groups: (string * string[])[]
}

/// A named bundle of option defaults, so a sheet a repository already
/// bakes keeps one command. defli reproduces the tower-defense sheet the
/// Defli sample reads.
type Preset = {
  Name: string
  Xml: string
  Namespace: string
  Module: string
  SheetPath: string
  TileSize: bool
  Curation: string voption
  DictHelper: string voption
  Source: string voption
}

let usage() =
  printfn
    "usage: dotnet fsi tools/gen-tiles.fsx --xml <atlas.xml> --out <file.fs> --namespace <ns> [options]"

  printfn
    "       --module <name> --sheet-path <png> --tile-size --curation <file>"

  printfn "       --dict-helper <fn> --source <text> --preset defli --help"

let argv = fsi.CommandLineArgs |> Array.skip 1

let values = Dictionary<string, string>()
let switches = HashSet<string>()
let mutable index = 0
let mutable badArg = ValueNone

while index < argv.Length && badArg.IsNone do
  let arg = argv[index]

  if arg = "--help" || arg = "-h" then
    switches.Add "--help" |> ignore
    index <- index + 1
  elif arg = "--tile-size" then
    switches.Add arg |> ignore
    index <- index + 1
  elif arg.StartsWith "--" then
    if index + 1 >= argv.Length then
      badArg <- ValueSome $"'{arg}' wants a value"
    else
      values[arg] <- argv[index + 1]
      index <- index + 2
  else
    badArg <- ValueSome $"unexpected argument '{arg}'"

let known = [
  "--xml"
  "--out"
  "--namespace"
  "--module"
  "--sheet-path"
  "--curation"
  "--dict-helper"
  "--source"
  "--preset"
]

for key in values.Keys do
  if not(List.contains key known) && badArg.IsNone then
    badArg <- ValueSome $"unknown option '{key}'"

match badArg with
| ValueSome reason ->
  eprintfn "gen-tiles: %s" reason
  usage()
  exit 1
| ValueNone -> ()

if switches.Contains "--help" then
  usage()
  exit 0

let value(key: string) : string voption =
  match values.TryGetValue key with
  | true, v -> ValueSome v
  | false, _ -> ValueNone

// ── presets ──────────────────────────────────────────────────

/// The tower-defense sheet Defli bakes. The namespace is the one the
/// committed catalog declares.
let defliPreset = {
  Name = "defli"
  Xml = "assets/kenney_tower-defense-top-down/towerDefense_tilesheet.xml"
  Namespace = "Defli.State"
  Module = "Tiles"
  SheetPath = "kenney_tower-defense-top-down/towerDefense_tilesheet.png"
  TileSize = true
  Curation = ValueSome "Defli/Shared/State/Tiles.curation"
  DictHelper = ValueSome "Defli.FrozenDict.tryGetValue"
  Source =
    ValueSome "assets/kenney_tower-defense-top-down/towerDefense_tilesheet.xml"
}

let presets = [ defliPreset ]

let preset =
  match value "--preset" with
  | ValueSome name ->
    match presets |> List.tryFind(fun p -> p.Name = name) with
    | Some p -> ValueSome p
    | None ->
      eprintfn
        "gen-tiles: unknown preset '%s' (known: %s)"
        name
        (presets |> List.map(fun p -> p.Name) |> String.concat ", ")

      exit 1
  | ValueNone -> ValueNone

let fromPreset (pick: Preset -> 'T) (fallback: 'T) : 'T =
  match preset with
  | ValueSome p -> pick p
  | ValueNone -> fallback

// ── the resolved options ─────────────────────────────────────

let xml =
  match value "--xml" with
  | ValueSome v -> v
  | ValueNone -> fromPreset (fun p -> p.Xml) ""

let outPath =
  match value "--out" with
  | ValueSome v -> v
  | ValueNone -> ""

let ns =
  match value "--namespace" with
  | ValueSome v -> v
  | ValueNone -> fromPreset (fun p -> p.Namespace) ""

let moduleName =
  match value "--module" with
  | ValueSome v -> v
  | ValueNone -> fromPreset (fun p -> p.Module) "Tiles"

let sheetPath =
  match value "--sheet-path" with
  | ValueSome v -> ValueSome v
  | ValueNone ->
    match preset with
    | ValueSome p -> ValueSome p.SheetPath
    | ValueNone ->
      if xml = "" then
        ValueNone
      else
        ValueSome(Path.GetFileNameWithoutExtension xml + ".png")

let curationArg = value "--curation"

let dictHelper =
  match value "--dict-helper" with
  | ValueSome v -> ValueSome v
  | ValueNone -> fromPreset (fun p -> p.DictHelper) ValueNone

let source =
  match value "--source" with
  | ValueSome v -> ValueSome v
  | ValueNone -> fromPreset (fun p -> p.Source) ValueNone

let tileSize =
  switches.Contains "--tile-size" || fromPreset (fun p -> p.TileSize) false

// ── required arguments ───────────────────────────────────────

let missing = [
  if xml = "" then
    "xml"
  if outPath = "" then
    "out"
  if ns = "" then
    "namespace"
  if sheetPath.IsNone then
    "sheet-path"
]

if not missing.IsEmpty then
  eprintfn
    "gen-tiles: missing required option(s): %s"
    (String.concat ", " missing)

  usage()
  exit 1

// ── reading ──────────────────────────────────────────────────

let readAtlas(path: string) : Tile[] =
  if not(File.Exists path) then
    eprintfn "gen-tiles: no atlas XML at '%s'" path
    exit 1

  let doc = XDocument.Load path

  doc.Descendants(XName.Get "SubTexture")
  |> Seq.map(fun e ->
    let attr(name: string) = e.Attribute(XName.Get name).Value

    {
      Name = (attr "name").Replace(".png", "")
      X = int(attr "x")
      Y = int(attr "y")
      W = int(attr "width")
      H = int(attr "height")
    })
  |> Seq.toArray

/// Reads binding and group lines. Every line is one of those, a comment,
/// or blank; anything else fails, so a typo in a curation file never
/// silently drops a name.
let readCuration(path: string) : Curation =
  if not(File.Exists path) then
    eprintfn "gen-tiles: no curation file at '%s'" path
    exit 1

  let named = ResizeArray<string * string>()
  let groups = ResizeArray<string * string[]>()

  for raw in File.ReadAllLines path do
    let line = raw.Trim()

    if line <> "" && not(line.StartsWith "#") then
      // the head is the keyword and the name; everything after '=' is
      // the payload, so a binding's atlas name and a group's member
      // list read the same way
      let eq = line.IndexOf '='

      if eq < 0 then
        eprintfn "gen-tiles: bad curation line: %s" line
        exit 1

      let head =
        line
          .Substring(0, eq)
          .Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

      let payload = line.Substring(eq + 1).Trim()

      match head with
      | [| "binding"; name |] ->
        if payload = "" then
          eprintfn "gen-tiles: binding '%s' names no tile" name
          exit 1

        named.Add(name, payload)
      | [| "group"; name |] ->
        let members =
          payload.Split(',')
          |> Array.map(fun m -> m.Trim())
          |> Array.filter(fun m -> m <> "")

        if members.Length = 0 then
          eprintfn "gen-tiles: group '%s' has no members" name
          exit 1

        groups.Add(name, members)
      | _ ->
        eprintfn "gen-tiles: bad curation line: %s" line
        exit 1

  {
    Named = named.ToArray()
    Groups = groups.ToArray()
  }

let tiles = readAtlas xml

let curation =
  match curationArg with
  | ValueSome path -> readCuration path
  | ValueNone ->
    (match preset with
     | ValueSome p ->
       (match p.Curation with
        | ValueSome path -> readCuration path
        | ValueNone -> { Named = [||]; Groups = [||] })
     | ValueNone -> { Named = [||]; Groups = [||] })

// ── emitting ─────────────────────────────────────────────────

let regenerate =
  let parts = [
    "dotnet fsi tools/gen-tiles.fsx"
    $"--xml {xml}"
    $"--out {outPath}"
    $"--namespace {ns}"
    $"--module {moduleName}"
    match sheetPath with
    | ValueSome p -> $"--sheet-path {p}"
    | ValueNone -> ()
    if tileSize then
      "--tile-size"
    match curationArg with
    | ValueSome p -> $"--curation {p}"
    | ValueNone -> ()
    match dictHelper with
    | ValueSome f -> $"--dict-helper {f}"
    | ValueNone -> ()
  ]

  String.concat " " parts

let sb = System.Text.StringBuilder()
let line(s: string) = sb.AppendLine s |> ignore
let linef fmt = Printf.ksprintf line fmt

line "// ─────────────────────────────────────────────────────────────"
line "// GENERATED by tools/gen-tiles.fsx — DO NOT EDIT BY HAND."
linef "// Regenerate with:  %s" regenerate

match source with
| ValueSome s -> linef "// Sources: %s" s
| ValueNone -> ()

line "// ─────────────────────────────────────────────────────────────"
linef "namespace %s" ns
line ""
line "open System.Collections.Frozen"
line "open System.Collections.Generic"
line ""
linef "module %s =" moduleName
line ""

match sheetPath with
| ValueSome p ->
  line "  [<Literal>]"
  linef "  let SheetPath = %A" p
  line ""
| ValueNone -> ()

if tileSize && tiles.Length > 0 then
  line "  [<Literal>]"
  linef "  let TileSize = %d" tiles[0].W
  line ""

linef
  "  /// All %d atlas tiles (name, position, size) baked at compile time."
  tiles.Length

line
  "  /// Canonical store: ordered and iterable. The name index is built from it."

line "  let all: TileInfo[] = [|"

for t in tiles do
  linef
    "    { Name = %A; X = %d; Y = %d; Width = %d; Height = %d }"
    t.Name
    t.X
    t.Y
    t.W
    t.H

line "  |]"
line ""
line "  /// O(1) name index over the baked dataset (built once at module init)."
line "  let byName: FrozenDictionary<string, TileInfo> ="
line "    all"
line "    |> Seq.map(fun t -> KeyValuePair(t.Name, t))"
line "    |> FrozenDictionary.ToFrozenDictionary"
line ""

line
  "  /// Safe name lookup for data-driven code (map documents, procedural gen)."

match dictHelper with
| ValueSome fn ->
  linef "  let tryByName(name: string) : TileInfo voption = %s name byName" fn
| ValueNone ->
  line "  let tryByName(name: string) : TileInfo voption ="
  line "    match byName.TryGetValue name with"
  line "    | true, info -> ValueSome info"
  line "    | false, _ -> ValueNone"

if curation.Named.Length > 0 then
  line ""
  line "  // ── Semantically named tiles (curated in the generator's input) ──"

  for semantic, atlasName in curation.Named do
    let tile =
      tiles
      |> Array.tryFind(fun t -> t.Name = atlasName)
      |> Option.defaultWith(fun () ->
        eprintfn "gen-tiles: tile not found in atlas: %s" atlasName
        exit 1)

    linef
      "  /// %s — atlas position (%d, %d), %dx%d."
      atlasName
      tile.X
      tile.Y
      tile.W
      tile.H

    linef "  let %s = byName[%A]" semantic atlasName

if curation.Groups.Length > 0 then
  line ""
  line "  // ── Groups (curated in the generator's input) ──"

  for group, members in curation.Groups do
    for m in members do
      if not(Array.exists (fun (n, _) -> n = m) curation.Named) then
        eprintfn "gen-tiles: group '%s' names unknown binding '%s'" group m
        exit 1

    linef "  let %s = [| %s |]" group (members |> String.concat "; ")

line ""

let directory = Path.GetDirectoryName outPath

if directory <> "" then
  Directory.CreateDirectory directory |> ignore

File.WriteAllText(outPath, sb.ToString())

printfn
  "%s: %d tiles, %d named, %d groups"
  moduleName
  tiles.Length
  curation.Named.Length
  curation.Groups.Length

printfn "Wrote %s" outPath
