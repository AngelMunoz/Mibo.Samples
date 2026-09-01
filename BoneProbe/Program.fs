module BoneProbe.Program

open System
open BoneProbe.Scene

let private printUsage() =
  eprintfn
    "Usage: dotnet run --project BoneProbe -- [raw|palette|dimensions|slope] <path> [-v|--verbosity full|summary] [-f|--focus <name>]"

  eprintfn
    "       dotnet run --project BoneProbe -- xbones <content-root> <asset-name> [raw-bone-count]"

  eprintfn
    "       dotnet run --project BoneProbe -- emit <models-dir> <output.fs> --namespace <ns> [options]"

  eprintfn ""
  eprintfn "Commands:"

  eprintfn
    "  raw         Dump raw Assimp scene (meshes, bones, animation channels)."

  eprintfn
    "  palette     Build the Mibo.MonoGame bone palette and verify the bind-pose invariant."

  eprintfn
    "  dimensions  Batch report: per-model vertex extents + animation count (dir or .glb file)."

  eprintfn
    "  slope       Report a slope tile's ramp Y range and high-edge direction."

  eprintfn
    "  emit        Bake model extents into a project-agnostic F# dataset."

  eprintfn ""
  eprintfn "Options:"

  eprintfn
    "  -v, --verbosity <full|summary>  Output detail level (default: full)."

  eprintfn "  -f, --focus <name>              Filter records by name substring."

  eprintfn ""
  eprintfn "Emit options:"

  eprintfn "  --namespace <ns>     Required. Namespace of the generated module."

  eprintfn "  --base-path <p>      Content asset prefix (Path = p/rel)."

  eprintfn
    "  --root <dir>         Dir rel-paths are computed from (default: models-dir)."

  eprintfn "  --recursive          Scan subdirectories too."

  eprintfn
    "  --exclude <prefix>   Skip filenames starting with prefix (repeatable)."

  eprintfn "  --type <name>        Record type name (default: ModelInfo)."

  eprintfn
    "  --define-type        Emit the record in the output (default: assume it exists)."

  eprintfn "  --fsi                Also write <output>.fsi."

  eprintfn
    "  --dict-helper <fn>   Fully-qualified tryGetValue (default: self-contained)."

  eprintfn
    "  --curation <file>    Semantic bindings + groups (see Emit.fs header)."

  eprintfn "  --pattern <glob>     File search pattern (default: *.glb)."

let private parseVerbosity(arg: string) : Verbosity option =
  match arg.ToLower() with
  | "full" -> Some Full
  | "summary" -> Some Summary
  | _ -> None

let rec private parseOptions
  (args: string list)
  (acc: Options option)
  : Options option =
  match args, acc with
  | [], Some opts -> Some opts
  | [], None -> None
  | ("raw" :: path :: rest), None ->
    let opts = {
      Mode = Raw
      Path = path
      OutputPath = ""
      Verbosity = Full
      Focus = None
    }

    parseOptions rest (Some opts)
  | ("palette" :: path :: rest), None ->
    let opts = {
      Mode = Palette
      Path = path
      OutputPath = ""
      Verbosity = Full
      Focus = None
    }

    parseOptions rest (Some opts)
  | ("xbones" :: contentRoot :: asset :: rest), None ->
    // Optional trailing positional: the raw skeleton's bone count, so used
    // content bones at/after it get flagged in the dump.
    let rawBoneCount =
      match rest with
      | [ n ] ->
        match Int32.TryParse n with
        | true, v -> Some v
        | false, _ -> None
      | _ -> None

    let opts = {
      Mode = Xbones
      Path = contentRoot
      OutputPath = asset
      Verbosity = Full
      Focus = rawBoneCount |> Option.map string
    }

    Some opts
  | ("dimensions" :: path :: rest), None ->
    let opts = {
      Mode = Dimensions
      Path = path
      OutputPath = ""
      Verbosity = Full
      Focus = None
    }

    parseOptions rest (Some opts)
  | ("slope" :: path :: rest), None ->
    let opts = {
      Mode = Slope
      Path = path
      OutputPath = ""
      Verbosity = Full
      Focus = None
    }

    parseOptions rest (Some opts)
  | ("emit" :: modelsDir :: output :: rest), None ->
    let opts = {
      Mode = Emit
      Path = modelsDir
      OutputPath = output
      Verbosity = Full
      Focus = None
    }

    parseOptions rest (Some opts)
  | ("-v" :: v :: rest), Some opts ->
    match parseVerbosity v with
    | Some verb -> parseOptions rest (Some { opts with Verbosity = verb })
    | None -> None
  | ("--verbosity" :: v :: rest), Some opts ->
    match parseVerbosity v with
    | Some verb -> parseOptions rest (Some { opts with Verbosity = verb })
    | None -> None
  | ("-f" :: name :: rest), Some opts ->
    parseOptions rest (Some { opts with Focus = Some name })
  | ("--focus" :: name :: rest), Some opts ->
    parseOptions rest (Some { opts with Focus = Some name })
  // Emit flags are parsed separately (parseEmitFlags); stop here so
  // the catch-all below does not reject them.
  | (flag :: _), Some { Mode = Emit } when flag.StartsWith "-" -> acc
  | _ -> None

/// Emit-flag state accumulated while walking the tail arguments.
type private EmitAcc() =
  member val Namespace = "" with get, set
  member val BasePath = "" with get, set
  member val Root = "" with get, set
  member val Recursive = false with get, set
  member val Excludes = ResizeArray<string>() with get, set
  member val TypeName = "ModelInfo" with get, set
  member val DefineType = false with get, set
  member val EmitFsi = false with get, set
  member val DictHelper = "" with get, set
  member val Curation = "" with get, set
  member val Pattern = "" with get, set
  member val Bad = false with get, set

let rec private parseEmitFlags (args: string list) (acc: EmitAcc) : EmitAcc =
  match args with
  | [] -> acc
  | "--namespace" :: v :: rest ->
    acc.Namespace <- v
    parseEmitFlags rest acc
  | "--base-path" :: v :: rest ->
    acc.BasePath <- v
    parseEmitFlags rest acc
  | "--root" :: v :: rest ->
    acc.Root <- v
    parseEmitFlags rest acc
  | "--recursive" :: rest ->
    acc.Recursive <- true
    parseEmitFlags rest acc
  | "--exclude" :: v :: rest ->
    acc.Excludes.Add v
    parseEmitFlags rest acc
  | "--type" :: v :: rest ->
    acc.TypeName <- v
    parseEmitFlags rest acc
  | "--define-type" :: rest ->
    acc.DefineType <- true
    parseEmitFlags rest acc
  | "--fsi" :: rest ->
    acc.EmitFsi <- true
    parseEmitFlags rest acc
  | "--dict-helper" :: v :: rest ->
    acc.DictHelper <- v
    parseEmitFlags rest acc
  | "--curation" :: v :: rest ->
    acc.Curation <- v
    parseEmitFlags rest acc
  | "--pattern" :: v :: rest ->
    acc.Pattern <- v
    parseEmitFlags rest acc
  | other :: _ ->
    eprintfn $"unknown emit flag: {other}"
    acc.Bad <- true
    acc

/// The flag tail after the emit mode's two positional arguments.
let private emitTail(args: string list) : string list =
  match args with
  | "emit" :: _ :: _ :: rest -> rest
  | _ -> []

[<EntryPoint>]
let main argv =
  match parseOptions (Array.toList argv) None with
  | None ->
    printUsage()
    1
  | Some opts ->
    match opts.Mode with
    | Raw -> BoneProbe.RawAssimp.probe opts
    | Palette -> BoneProbe.Palette.probe opts
    | Dimensions -> BoneProbe.Dimensions.probe opts
    | Slope -> BoneProbe.Slope.probe opts.Path
    | Xbones ->
      let rawBoneCount =
        opts.Focus
        |> Option.bind(fun f ->
          match Int32.TryParse f with
          | true, n -> Some n
          | false, _ -> None)

      BoneProbe.Xnb.probe opts.Path opts.OutputPath rawBoneCount
    | Emit ->
      let acc = parseEmitFlags (emitTail(Array.toList argv)) (EmitAcc())

      if acc.Bad || acc.Namespace = "" then
        if acc.Namespace = "" then
          eprintfn "emit: --namespace is required"

        printUsage()
        1
      else
        BoneProbe.Emit.run {
          ModelsDir = opts.Path
          OutputPath = opts.OutputPath
          Namespace = acc.Namespace
          BasePath = acc.BasePath
          Root = if acc.Root = "" then opts.Path else acc.Root
          Recursive = acc.Recursive
          Excludes = acc.Excludes.ToArray()
          TypeName = acc.TypeName
          DefineType = acc.DefineType
          EmitFsi = acc.EmitFsi
          DictHelper = acc.DictHelper
          Curation = acc.Curation
          Pattern = acc.Pattern
        }
