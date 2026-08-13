module BoneProbe.Program

open System
open BoneProbe.Scene

let private printUsage() =
  eprintfn
    "Usage: dotnet run --project BoneProbe -- [raw|palette|dimensions] <path> [-v|--verbosity full|summary] [-f|--focus <name>]"

  eprintfn
    "       dotnet run --project BoneProbe -- emit <models-dir> <output.fs>"

  eprintfn ""
  eprintfn "Commands:"

  eprintfn
    "  raw         Dump raw Assimp scene (meshes, bones, animation channels)."

  eprintfn
    "  palette     Build the Mibo.MonoGame bone palette and verify the bind-pose invariant."

  eprintfn
    "  dimensions  Batch report: per-model vertex extents + animation count (dir or .glb file)."

  eprintfn
    "  emit        Bake model extents into an F# dataset (Defli3D.State.Models, e.g. Models.fs)."

  eprintfn ""
  eprintfn "Options:"

  eprintfn
    "  -v, --verbosity <full|summary>  Output detail level (default: full)."

  eprintfn "  -f, --focus <name>              Filter records by name substring."

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
  | ("dimensions" :: path :: rest), None ->
    let opts = {
      Mode = Dimensions
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
  | _ -> None

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
    | Emit -> BoneProbe.Emit.run opts.Path opts.OutputPath
