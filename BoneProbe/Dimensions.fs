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

/// Per-mesh scan detail (full verbosity only).
[<Struct>]
type MeshReport = {
  MeshName: string
  NodeName: string
  ChainTranslation: struct (float32 * float32 * float32)
  ChainScale: struct (float32 * float32 * float32)
  LocalMinY: float32
  LocalMaxY: float32
}

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
  /// Raw mesh-local min/max per axis (Y = up).
  RawMin: struct (float32 * float32 * float32)
  RawMax: struct (float32 * float32 * float32)
  /// Bounds after applying the scene-graph node transforms (what a
  /// renderer that bakes node transforms actually draws).
  SceneMin: struct (float32 * float32 * float32)
  SceneMax: struct (float32 * float32 * float32)
  /// Per-mesh detail: mesh name, node chain translations/scales,
  /// raw local Y range.
  Meshes: MeshReport[]
}


/// Approximate per-axis scale of a matrix (row lengths — node
/// transforms use the System.Numerics row-vector convention).
let private scaleOf(m: System.Numerics.Matrix4x4) =
  struct (System.Numerics.Vector3(m.M11, m.M12, m.M13).Length(),
          System.Numerics.Vector3(m.M21, m.M22, m.M23).Length(),
          System.Numerics.Vector3(m.M31, m.M32, m.M33).Length())

/// Bounds accumulator for the raw/scene fold below.
type private BoundsAcc() =
  member val MinX = Single.MaxValue with get, set
  member val MinY = Single.MaxValue with get, set
  member val MinZ = Single.MaxValue with get, set
  member val MaxX = Single.MinValue with get, set
  member val MaxY = Single.MinValue with get, set
  member val MaxZ = Single.MinValue with get, set
  member val Any = false with get, set

  member this.Add(x: float32, y: float32, z: float32) =
    this.Any <- true

    if x < this.MinX then
      this.MinX <- x

    if x > this.MaxX then
      this.MaxX <- x

    if y < this.MinY then
      this.MinY <- y

    if y > this.MaxY then
      this.MaxY <- y

    if z < this.MinZ then
      this.MinZ <- z

    if z > this.MaxZ then
      this.MaxZ <- z

  member this.Min = struct (this.MinX, this.MinY, this.MinZ)
  member this.Max = struct (this.MaxX, this.MaxY, this.MaxZ)

/// Full measurement of one loaded scene: raw mesh-local bounds,
/// scene-graph-transformed bounds, and per-mesh detail.
let private measureSceneFull(scene: Scene) =
  let raw = BoundsAcc()
  let world = BoundsAcc()
  let meshes = ResizeArray<MeshReport>()

  let rec walk (node: Node) (parent: System.Numerics.Matrix4x4) =
    // Row-vector convention: world = v * node * parent.
    let m = node.Transform * parent

    for mi in node.MeshIndices do
      let mesh = scene.Meshes[mi]
      let mutable minY = Single.MaxValue
      let mutable maxY = Single.MinValue

      for vi = 0 to mesh.VertexCount - 1 do
        let v = mesh.Vertices[vi]
        raw.Add(v.X, v.Y, v.Z)

        if v.Y < minY then
          minY <- v.Y

        if v.Y > maxY then
          maxY <- v.Y

        let w =
          System.Numerics.Vector3.Transform(
            System.Numerics.Vector3(v.X, v.Y, v.Z),
            m
          )

        world.Add(w.X, w.Y, w.Z)

      meshes.Add {
        MeshName = mesh.Name
        NodeName = node.Name
        ChainTranslation = struct (m.M41, m.M42, m.M43)
        ChainScale = scaleOf m
        LocalMinY = minY
        LocalMaxY = maxY
      }

    for child in node.Children do
      walk child m

  walk scene.RootNode System.Numerics.Matrix4x4.Identity

  struct (raw, world, meshes.ToArray())

/// Scan one .glb into a ModelReport. Owns its own AssimpContext via
/// Scene.tryLoad, so it is safe to call concurrently.
let private scanOne(path: string) : ModelReport =
  let name = Path.GetFileName path

  let empty error loaded scene = {
    Name = name
    Loaded = loaded
    SizeX = 0.0f
    SizeY = 0.0f
    SizeZ = 0.0f
    MeshCount = scene
    AnimationCount = 0
    Error = error
    RawMin = struct (0f, 0f, 0f)
    RawMax = struct (0f, 0f, 0f)
    SceneMin = struct (0f, 0f, 0f)
    SceneMax = struct (0f, 0f, 0f)
    Meshes = [||]
  }

  match tryLoad path with
  | ValueNone -> empty "load failed" false 0
  | ValueSome scene ->
    let struct (raw, world, meshes) = measureSceneFull scene

    if not raw.Any then
      {
        empty "no vertices" true scene.MeshCount with
            AnimationCount = scene.AnimationCount
      }
    else
      let struct (minX, minY, minZ) = raw.Min
      let struct (maxX, maxY, maxZ) = raw.Max

      {
        Name = name
        Loaded = true
        SizeX = maxX - minX
        SizeY = maxY - minY
        SizeZ = maxZ - minZ
        MeshCount = scene.MeshCount
        AnimationCount = scene.AnimationCount
        Error = ""
        RawMin = raw.Min
        RawMax = raw.Max
        SceneMin = world.Min
        SceneMax = world.Max
        Meshes = meshes
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

let private printDetail(r: ModelReport) =
  let struct (rawMinX, rawMinY, rawMinZ) = r.RawMin
  let struct (rawMaxX, rawMaxY, rawMaxZ) = r.RawMax
  let struct (scMinX, scMinY, scMinZ) = r.SceneMin
  let struct (scMaxX, scMaxY, scMaxZ) = r.SceneMax

  printfn $"{r.Name}:"

  printfn
    $"  raw   min ({rawMinX:F3}, {rawMinY:F3}, {rawMinZ:F3})  max ({rawMaxX:F3}, {rawMaxY:F3}, {rawMaxZ:F3})"

  printfn
    $"  scene min ({scMinX:F3}, {scMinY:F3}, {scMinZ:F3})  max ({scMaxX:F3}, {scMaxY:F3}, {scMaxZ:F3})"

  for m in r.Meshes do
    let struct (tx, ty, tz) = m.ChainTranslation
    let struct (sx, sy, sz) = m.ChainScale

    printfn
      $"  mesh {m.MeshName} (node {m.NodeName}): localY {m.LocalMinY:F3}..{m.LocalMaxY:F3}  nodeT ({tx:F3}, {ty:F3}, {tz:F3})  nodeS ({sx:F3}, {sy:F3}, {sz:F3})"

let private printReport (verbosity: Verbosity) (reports: ModelReport[]) =
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

  if verbosity = Full then
    for r in reports do
      if r.Loaded && r.Error = "" then
        printDetail r

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

    printReport options.Verbosity reports
    0
