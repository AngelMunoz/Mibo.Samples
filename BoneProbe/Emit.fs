module BoneProbe.Emit

open System
open System.Globalization
open System.IO
open System.Text
open BoneProbe.Dimensions
open BoneProbe.Scene

// --------------------------------------------------------------
// Dataset emitter (project-agnostic)
//
// Bakes measured model extents (mesh-local vertex extents, same
// Assimp flag set as the dimensions report) into a compile-time
// F# dataset for ANY project. Everything project-specific comes
// from the command line or the curation file:
//
//   dotnet run --project BoneProbe -- emit <models-dir> <output.fs>
//       --namespace <ns>          required
//       --base-path <p>           content asset prefix (Path = p/rel)
//       --root <dir>              dir rel-paths are computed from
//                                  (default: models-dir)
//       --recursive               scan subdirectories too
//       --exclude <prefix>        skip filenames starting with the
//                                  prefix (repeatable)
//       --type <name>             record type name (default ModelInfo)
//       --define-type             emit the record in the output
//                                  (default: assume it exists in
//                                  the namespace)
//       --fsi                     also write <output>.fsi
//       --dict-helper <fn>        fully-qualified tryGetValue of
//                                  (string -> dict -> v voption);
//                                  default: self-contained
//       --curation <file>         semantic bindings + groups
//
// Curation file format (all paths relative to --root, no
// extension; '#'-prefixed lines are comments):
//
//   binding grass = tiles/base/hex_grass
//   group slopeTiles = grassSlopeLow, grassSlopeHigh
//
// Groups may only reference bindings (typo guard, same as the
// dataset itself: a group naming an unknown binding aborts).
// --------------------------------------------------------------

/// Emitter configuration, fully derived from the command line.
type EmitOptions = {
  ModelsDir: string
  OutputPath: string
  Namespace: string
  BasePath: string
  Root: string
  Recursive: bool
  Excludes: string[]
  TypeName: string
  DefineType: bool
  EmitFsi: bool
  /// Empty = self-contained TryGetValue match.
  DictHelper: string
  /// Empty = no curated bindings or groups.
  Curation: string
  /// File search pattern (empty = *.glb).
  Pattern: string
}

/// One `binding name = relpath` line.
type private Binding = { Name: string; RelPath: string }

/// One `group name = binding, binding` line.
type private Group = { Name: string; Members: string[] }

type private Curation = { Bindings: Binding[]; Groups: Group[] }

let private parseCuration(path: string) : Curation =
  let bindings = ResizeArray<Binding>()
  let groups = ResizeArray<Group>()

  for line in File.ReadAllLines path do
    let trimmed = line.Trim()

    if not(String.IsNullOrEmpty trimmed) && not(trimmed.StartsWith "#") then
      match trimmed.Split('=') with
      | [| lhs; rhs |] ->
        let lhsParts = lhs.Trim().Split(' ', 2)

        match lhsParts[0] with
        | "binding" when lhsParts.Length = 2 ->
          bindings.Add {
            Binding.Name = lhsParts[1].Trim()
            RelPath = rhs.Trim().Replace('\\', '/')
          }
        | "group" when lhsParts.Length = 2 ->
          groups.Add {
            Group.Name = lhsParts[1].Trim()
            Members =
              rhs.Split(',')
              |> Array.map(fun m -> m.Trim())
              |> Array.filter(fun m -> not(String.IsNullOrEmpty m))
          }
        | other -> failwithf $"unknown curation keyword: {other}"
      | _ -> failwithf $"malformed curation line: {trimmed}"

  {
    Bindings = bindings.ToArray()
    Groups = groups.ToArray()
  }

/// Format a measured extent as a float32 literal, rounded to 3 decimals
/// (matches the Dimensions report's F3 output, minus trailing zeros).
let private formatSize(v: float32) =
  MathF.Round(v, 3).ToString("0.0##", CultureInfo.InvariantCulture) + "f"

/// The canonical regenerate command, rebuilt from the options so the
/// header always matches how the file was produced.
let private regenCommand(o: EmitOptions) =
  let sb = StringBuilder()

  sb.Append
    $"dotnet run --project BoneProbe -- emit {o.ModelsDir} {o.OutputPath}"
  |> ignore

  sb.Append $" --namespace {o.Namespace}" |> ignore
  sb.Append $" --base-path {o.BasePath}" |> ignore

  if o.Root <> o.ModelsDir then
    sb.Append $" --root {o.Root}" |> ignore

  if o.Recursive then
    sb.Append " --recursive" |> ignore

  for e in o.Excludes do
    sb.Append $" --exclude {e}" |> ignore

  if o.TypeName <> "ModelInfo" then
    sb.Append $" --type {o.TypeName}" |> ignore

  if o.DefineType then
    sb.Append " --define-type" |> ignore

  if o.EmitFsi then
    sb.Append " --fsi" |> ignore

  if o.DictHelper <> "" then
    sb.Append $" --dict-helper {o.DictHelper}" |> ignore

  if o.Curation <> "" then
    sb.Append $" --curation {o.Curation}" |> ignore

  if o.Pattern <> "" then
    sb.Append $" --pattern {o.Pattern}" |> ignore

  sb.ToString()

let private typeDecl (o: EmitOptions) (indent: string) =
  let line l = $"{indent}{l}"

  [|
    line "[<Struct>]"
    line $"type {o.TypeName} = {{"
    line "  Name: string"
    line "  Path: string"
    line "  SizeX: float32"
    line "  SizeY: float32"
    line "  SizeZ: float32"
    line "}"
  |]

/// Build the generated .fs text for the measured reports.
let private emitFs
  (o: EmitOptions)
  (entries: (string * string * ModelReport)[])
  (curation: Curation)
  =
  let byRelPath = entries |> Array.map(fun (rel, _, _) -> rel) |> Set.ofArray

  let byNameOf(rel: string) =
    entries
    |> Array.tryFind(fun (r, _, _) -> r = rel)
    |> Option.defaultWith(fun () ->
      failwithf $"curated model not found in scan: {rel}")

  let bindingNames =
    curation.Bindings |> Array.map(fun b -> b.Name) |> Set.ofArray

  for g in curation.Groups do
    for m in g.Members do
      if not(Set.contains m bindingNames) then
        failwithf $"group {g.Name} references unknown binding: {m}"

  for b in curation.Bindings do
    if not(Set.contains b.RelPath byRelPath) then
      failwithf $"binding {b.Name} references unknown model: {b.RelPath}"

  let sb = StringBuilder()
  let line(s: string) = sb.AppendLine s |> ignore
  let linef fmt = Printf.ksprintf line fmt

  let recursiveNote = if o.Recursive then ", recursive" else ""
  let pattern = if o.Pattern = "" then "*.glb" else o.Pattern
  /// Runtime asset path of a model: the content prefix + rel when a
  /// base path is given, the plain rel path otherwise.
  let pathOf rel = if o.BasePath = "" then rel else o.BasePath + "/" + rel

  line "// ─────────────────────────────────────────────────────────────"
  line "// GENERATED by BoneProbe emit — DO NOT EDIT BY HAND."
  line $"// Regenerate with:  {regenCommand o}"

  line
    $"// Sources: {o.Root}/{pattern} ({entries.Length} models{recursiveNote})"

  line "// ─────────────────────────────────────────────────────────────"
  line $"namespace {o.Namespace}"
  line ""
  line "open System.Collections.Frozen"
  line "open System.Collections.Generic"
  line ""

  if o.DefineType then
    for l in typeDecl o "" do
      line l

    line ""
  else
    line $"// {o.TypeName} is hand-defined in this namespace."

  line ""
  line "module Models ="
  line ""
  line "  [<Literal>]"
  line $"  let BasePath = \"{o.BasePath}\""
  line ""

  line
    "  /// All models baked at compile time (mesh-local extents, sorted by name)."

  line
    "  /// Canonical store: ordered and iterable. The name index is built from it."

  line $"  let all: {o.TypeName}[] = [|"

  for rel, name, r in entries |> Array.sortBy(fun (_, n, _) -> n) do
    linef
      "    { Name = %A; Path = %A; SizeX = %s; SizeY = %s; SizeZ = %s }"
      name
      (pathOf rel)
      (formatSize r.SizeX)
      (formatSize r.SizeY)
      (formatSize r.SizeZ)

  line "  |]"
  line ""

  line
    "  /// O(1) name index over the baked dataset (built once at module init)."

  line $"  let byName: FrozenDictionary<string, {o.TypeName}> ="
  line "    all"
  line "    |> Seq.map (fun t -> KeyValuePair(t.Name, t))"
  line "    |> FrozenDictionary.ToFrozenDictionary"
  line ""

  line "  /// Safe name lookup for data-driven code."

  if o.DictHelper = "" then
    line $"  let tryByName (name: string) : {o.TypeName} voption ="
    line "    match byName.TryGetValue name with"
    line "    | true, info -> ValueSome info"
    line "    | false, _ -> ValueNone"
  else
    line $"  let inline tryByName (name: string) : {o.TypeName} voption ="
    line $"    {o.DictHelper} name byName"

  line ""

  if curation.Bindings.Length > 0 then
    line
      $"  // ── Semantically named models (curated in {Path.GetFileName o.Curation}) ──"

    line ""

    for b in curation.Bindings do
      let _, _, report = byNameOf b.RelPath

      linef
        "  /// %s — %s × %s × %s."
        b.RelPath
        (formatSize report.SizeX)
        (formatSize report.SizeY)
        (formatSize report.SizeZ)

      linef
        "  let %s = byName[%A]"
        b.Name
        (Path.GetFileNameWithoutExtension b.RelPath)

    line ""

  if curation.Groups.Length > 0 then
    line "  // ── Groups (curated) ──"
    line ""

    for g in curation.Groups do
      linef "  let %s = [| %s |]" g.Name (g.Members |> String.concat "; ")

    line ""

  sb.ToString()

/// Build the matching signature file text (same header, declarations only).
let private emitFsi (o: EmitOptions) (curation: Curation) =
  let sb = StringBuilder()
  let line(s: string) = sb.AppendLine s |> ignore

  line "// ─────────────────────────────────────────────────────────────"
  line "// GENERATED by BoneProbe emit — DO NOT EDIT BY HAND."
  line $"// Signature for the dataset emitted next to it (regenerate both)."
  line "// ─────────────────────────────────────────────────────────────"
  line $"namespace {o.Namespace}"
  line ""
  line "open System.Collections.Frozen"
  line ""

  if o.DefineType then
    for l in typeDecl o "" do
      line l

    line ""

  line "module Models ="
  line ""
  line "  [<Literal>]"
  line $"  val BasePath: string = \"{o.BasePath}\""
  line ""
  line $"  val all: {o.TypeName}[]"
  line ""
  line $"  val byName: FrozenDictionary<string, {o.TypeName}>"
  line ""
  line $"  val tryByName: name: string -> {o.TypeName} voption"
  line ""

  if curation.Bindings.Length > 0 then
    line "  // ── Semantically named models ──"
    line ""

    for b in curation.Bindings do
      line $"  val {b.Name}: {o.TypeName}"

    line ""

  if curation.Groups.Length > 0 then
    line "  // ── Groups ──"
    line ""

    for g in curation.Groups do
      line $"  val {g.Name}: {o.TypeName}[]"

    line ""

  sb.ToString()

/// Scan, measure, and write the dataset (and its signature when
/// --fsi is set). Returns 0 on success, 1 on failure.
let run(o: EmitOptions) : int =
  let search =
    if o.Recursive then
      SearchOption.AllDirectories
    else
      SearchOption.TopDirectoryOnly

  let patterns =
    if o.Pattern = "" then
      [| "*.glb" |]
    else
      o.Pattern.Split(',')
      |> Array.map(fun p -> p.Trim())
      |> Array.filter(fun p -> not(String.IsNullOrEmpty p))

  let files =
    try
      patterns
      |> Array.collect(fun p -> Directory.GetFiles(o.ModelsDir, p, search))
      |> Array.distinct
    with ex ->
      eprintfn
        $"could not scan models directory: {o.ModelsDir} ({ex.GetType().Name}: {ex.Message})"

      [||]

  let files =
    files
    |> Array.filter(fun f ->
      not(
        o.Excludes
        |> Array.exists(fun prefix -> Path.GetFileName(f).StartsWith prefix)
      ))

  match files with
  | [||] ->
    eprintfn $"No .glb files found at: {o.ModelsDir}"

    eprintfn
      "Usage: dotnet run --project BoneProbe -- emit <models-dir> <output.fs> --namespace <ns> ..."

    1
  | _ ->
    let entries =
      files
      |> Array.Parallel.map(fun f ->
        let report = scanOne f

        let rel =
          Path
            .GetRelativePath(o.Root, Path.ChangeExtension(f, null))
            .Replace('\\', '/')

        rel, Path.GetFileNameWithoutExtension f, report)
      |> Array.sortBy(fun (_, name, _) -> name)

    let failed = entries |> Array.filter(fun (_, _, r) -> not r.Loaded)
    let loaded = entries.Length - failed.Length

    if failed.Length > 0 then
      for _, name, r in failed do
        eprintfn $"failed to load: {name} ({r.Error})"

      eprintfn
        $"total={entries.Length} loaded={loaded} failed={failed.Length} — aborting, no output written"

      1
    else
      // Duplicate base names would collide in the byName index.
      let dupes =
        entries
        |> Array.groupBy(fun (_, name, _) -> name)
        |> Array.filter(fun (_, g) -> g.Length > 1)

      if dupes.Length > 0 then
        for name, _ in dupes do
          eprintfn $"duplicate model name across folders: {name}"

        1
      else
        let curation =
          if o.Curation = "" then
            { Bindings = [||]; Groups = [||] }
          else
            parseCuration o.Curation

        try
          let dir = Path.GetDirectoryName o.OutputPath

          if not(String.IsNullOrEmpty dir) then
            Directory.CreateDirectory dir |> ignore

          File.WriteAllText(o.OutputPath, emitFs o entries curation)

          if o.EmitFsi then
            File.WriteAllText(
              Path.ChangeExtension(o.OutputPath, ".fsi"),
              emitFsi o curation
            )

          let fsiNote = if o.EmitFsi then " + .fsi" else ""

          printfn
            $"total={entries.Length} loaded={loaded} failed={failed.Length}"

          printfn $"Wrote {o.OutputPath}{fsiNote}"
          0
        with ex ->
          eprintfn
            $"failed to write {o.OutputPath}: {ex.GetType().Name}: {ex.Message}"

          1
