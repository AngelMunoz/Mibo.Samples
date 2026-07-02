module BoneProbe.Program

open System
open BoneProbe.Scene

let private printUsage() =
  eprintfn
    "Usage: dotnet run --project BoneProbe -- [raw|palette] <path-to-glb> [-v|--verbosity full|summary] [-f|--focus <name>]"

  eprintfn ""
  eprintfn "Commands:"

  eprintfn
    "  raw      Dump raw Assimp scene (meshes, bones, animation channels)."

  eprintfn
    "  palette  Build the Mibo.MonoGame bone palette and verify the bind-pose invariant."

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
      Verbosity = Full
      Focus = None
    }

    parseOptions rest (Some opts)
  | ("palette" :: path :: rest), None ->
    let opts = {
      Mode = Palette
      Path = path
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
