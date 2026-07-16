module BoneProbe.Dimensions

open System
open System.IO
open Assimp
open BoneProbe.Scene

// --------------------------------------------------------------
// Model dimensions batch report
//
// Game-agnostic: scans a folder (or single file) of .glb models and
// reports each model's raw vertex extents (mesh-local — the same Assimp
// flag set as Mibo.MonoGame/Assets.fs, without PreTransformVertices, so
// vertices are never node-transform-baked) plus its embedded animation
// count. The caller derives cell footprints by dividing the reported size
// by the known cell unit (e.g. a "large" block reporting 2.0 vs a 1x1
// block reporting 1.0 => 2 cells).
//
// All files are scanned in parallel via Array.Parallel.map; each scan owns
// its own AssimpContext (Scene.tryLoad does `use importer = new
// AssimpContext()`), so there is no shared native importer. Results are
// collected, re-sorted by name, and the report is printed in one pass.
// --------------------------------------------------------------

/// Result of scanning one model file.
[<Struct>]
type ModelReport = {
  Name: string
  Loaded: bool
  SizeX: float32
  SizeY: float32
  SizeZ: float32
  MeshCount: int
  AnimationCount: int
  Error: string
}

/// Fold every vertex across every mesh into per-axis extents. Vertices are
/// mesh-local (same Assimp flag set as Mibo.MonoGame/Assets.fs, no
/// PreTransformVertices), so the extent equals the model's size in model
/// units. Returns ValueNone when the scene has no vertices at all.
let private measureScene(scene: Scene) : (float32 * float32 * float32) voption =
  let mutable minX, maxX = Single.MaxValue, Single.MinValue
  let mutable minY, maxY = Single.MaxValue, Single.MinValue
  let mutable minZ, maxZ = Single.MaxValue, Single.MinValue
  let mutable anyVertex = false

  for mi = 0 to scene.MeshCount - 1 do
    let mesh = scene.Meshes[mi]

    for vi = 0 to mesh.VertexCount - 1 do
      let v = mesh.Vertices[vi]
      anyVertex <- true
      let x, y, z = v.X, v.Y, v.Z

      if x < minX then
        minX <- x

      if x > maxX then
        maxX <- x

      if y < minY then
        minY <- y

      if y > maxY then
        maxY <- y

      if z < minZ then
        minZ <- z

      if z > maxZ then
        maxZ <- z

  if anyVertex then
    ValueSome(maxX - minX, maxY - minY, maxZ - minZ)
  else
    ValueNone

/// Scan one .glb into a ModelReport. Owns its own AssimpContext via
/// Scene.tryLoad, so it is safe to call concurrently.
let private scanOne(path: string) : ModelReport =
  let name = Path.GetFileName path

  match tryLoad path with
  | ValueNone -> {
      Name = name
      Loaded = false
      SizeX = 0.0f
      SizeY = 0.0f
      SizeZ = 0.0f
      MeshCount = 0
      AnimationCount = 0
      Error = "load failed"
    }
  | ValueSome scene ->
    match measureScene scene with
    | ValueNone -> {
        Name = name
        Loaded = true
        SizeX = 0.0f
        SizeY = 0.0f
        SizeZ = 0.0f
        MeshCount = scene.MeshCount
        AnimationCount = scene.AnimationCount
        Error = "no vertices"
      }
    | ValueSome(sx, sy, sz) ->
        {
          Name = name
          Loaded = true
          SizeX = sx
          SizeY = sy
          SizeZ = sz
          MeshCount = scene.MeshCount
          AnimationCount = scene.AnimationCount
          Error = ""
        }

/// Resolve the input path to a list of .glb files. A directory is scanned
/// for *.glb (top-level only); a single file is returned as-is. Returns
/// ValueNone when a directory holds no .glb. Existence is not pre-checked
/// (no File.Exists/Directory.Exists per repo convention): Directory.GetFiles
/// throws when `path` is not an existing directory, which is treated as the
/// single-file case; a missing file is surfaced later by Scene.tryLoad.
let private resolveFiles(path: string) : string[] voption =
  match
    try
      ValueSome(
        Directory.GetFiles(path, "*.glb", SearchOption.TopDirectoryOnly)
      )
    with _ ->
      ValueNone
  with
  | ValueSome [||] -> ValueNone
  | ValueSome files -> ValueSome files
  | ValueNone -> ValueSome [| path |]

let private printReport(reports: ModelReport[]) =
  let separator = String.replicate 95 "-"
  // String literals can't appear directly inside interpolation holes, so the
  // header labels are bound first.
  let hName, hSizeX, hSizeY, hSizeZ, hMeshes, hAnims =
    "model", "sizeX", "sizeY", "sizeZ", "meshes", "anims"

  printfn
    $"{hName, -44}{hSizeX, 12}{hSizeY, 12}{hSizeZ, 12}{hMeshes, 8}{hAnims, 7}"

  printfn "%s" separator

  for r in reports do
    if r.Loaded then
      printfn
        $"{r.Name, -44}{r.SizeX, 12:F3}{r.SizeY, 12:F3}{r.SizeZ, 12:F3}{r.MeshCount, 8}{r.AnimationCount, 7}"
    else
      printfn $"{r.Name, -44}{r.Error}"

  printfn "%s" separator

  let loaded = reports |> Array.filter(fun r -> r.Loaded)
  let failed = reports.Length - loaded.Length

  let animatable =
    loaded |> Array.filter(fun r -> r.AnimationCount > 0) |> Array.length

  printfn
    $"total={reports.Length} loaded={loaded.Length} animatable={animatable} failed={failed}"

/// Batch dimensions report. Scans every .glb in parallel (Array.Parallel.map),
/// collecting all results before printing the report once.
let probe(options: Options) : int =
  match resolveFiles options.Path with
  | ValueNone ->
    eprintfn $"No .glb files found at: {options.Path}"

    eprintfn
      "Usage: dotnet run --project BoneProbe -- dimensions <dir-or-glb-file>"

    1
  | ValueSome files ->
    // Sort input for a deterministic scan order, Array.Parallel.map (each
    // scanOne owns its own AssimpContext), collect, re-sort by name, print.
    let reports =
      files
      |> Array.sortBy Path.GetFileName
      |> Array.Parallel.map scanOne
      |> Array.sortBy(fun r -> r.Name)

    printReport reports
    0
