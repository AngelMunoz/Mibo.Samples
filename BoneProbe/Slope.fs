module BoneProbe.Slope

open System
open Assimp
open BoneProbe.Scene

// --------------------------------------------------------------
// Slope tile probe
//
// Measures a sloped tile's ramp in model space: the Y range it
// spans (ramp start/end) and the world-space angle of its HIGH
// edge - the direction the tile ramps up toward. Games bake this
// angle once into their direction tables instead of guessing.
//
// Usage: dotnet run --project BoneProbe -- slope <file.glb>
//
// Method: scene-transformed vertices (what a renderer draws);
// vertices within `epsilon` of maxY form the high-edge cluster;
// the cluster's mean XZ relative to the model's XZ center gives
// the direction. Flat tiles report no direction.
// --------------------------------------------------------------

/// Vertices this close to the top count as the high edge.
let private topEpsilon = 0.001f

type private Acc() =
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

let probe(path: string) : int =
  match tryLoad path with
  | ValueNone ->
    eprintfn $"load failed: {path}"
    1
  | ValueSome scene ->
    let bounds = Acc()
    let topX = ResizeArray<float32>()
    let topZ = ResizeArray<float32>()

    let rec walk (node: Node) (parent: System.Numerics.Matrix4x4) =
      // Row-vector convention: world = v * node * parent.
      let m = node.Transform * parent

      for mi in node.MeshIndices do
        let mesh = scene.Meshes[mi]

        for vi = 0 to mesh.VertexCount - 1 do
          let v = mesh.Vertices[vi]

          let w =
            System.Numerics.Vector3.Transform(
              System.Numerics.Vector3(v.X, v.Y, v.Z),
              m
            )

          bounds.Add(w.X, w.Y, w.Z)

      for child in node.Children do
        walk child m

    walk scene.RootNode System.Numerics.Matrix4x4.Identity

    if not bounds.Any then
      eprintfn "no vertices"
      1
    else
      let sizeX = bounds.MaxX - bounds.MinX
      let sizeY = bounds.MaxY - bounds.MinY
      let sizeZ = bounds.MaxZ - bounds.MinZ
      let centerX = (bounds.MinX + bounds.MaxX) / 2f
      let centerZ = (bounds.MinZ + bounds.MaxZ) / 2f

      // Second pass: collect the high-edge cluster now that the
      // bounds are known.
      let rec collect (node: Node) (parent: System.Numerics.Matrix4x4) =
        let m = node.Transform * parent

        for mi in node.MeshIndices do
          let mesh = scene.Meshes[mi]

          for vi = 0 to mesh.VertexCount - 1 do
            let v = mesh.Vertices[vi]

            let w =
              System.Numerics.Vector3.Transform(
                System.Numerics.Vector3(v.X, v.Y, v.Z),
                m
              )

            if w.Y >= bounds.MaxY - topEpsilon then
              topX.Add(w.X)
              topZ.Add(w.Z)

        for child in node.Children do
          collect child m

      collect scene.RootNode System.Numerics.Matrix4x4.Identity

      printfn $"{path}:"

      printfn
        $"  ramp Y: {bounds.MinY:F3} .. {bounds.MaxY:F3}  (height {sizeY:F3})"

      printfn
        $"  footprint: {sizeX:F3} x {sizeZ:F3}  center ({centerX:F3}, {centerZ:F3})"

      if sizeY < topEpsilon then
        printfn "  flat tile: no ramp direction"
      else
        let mutable meanX = 0f
        let mutable meanZ = 0f

        for x in topX do
          meanX <- meanX + x

        for z in topZ do
          meanZ <- meanZ + z

        meanX <- meanX / float32 topX.Count
        meanZ <- meanZ / float32 topZ.Count

        let dx = meanX - centerX
        let dz = meanZ - centerZ
        let angle = MathF.Atan2(dx, dz) * 180f / MathF.PI

        printfn
          $"  high edge: {topX.Count} top vertices, mean offset ({dx:F3}, {dz:F3})"

        printfn
          $"  ramps up toward {angle:F1} degrees (atan2 +X, +Z; 0 = +Z / South)"

        // Top-surface profile along the ramp axis: max Y per X bin
        // (8 bins across the footprint). Read as the surface height
        // from the low edge to the high edge - the ramp's actual
        // start and end heights.
        let bins = 8
        let binWidth = sizeX / float32 bins
        let profile = Array.create bins Single.MinValue

        let rec profileWalk (node: Node) (parent: System.Numerics.Matrix4x4) =
          let m = node.Transform * parent

          for mi in node.MeshIndices do
            let mesh = scene.Meshes[mi]

            for vi = 0 to mesh.VertexCount - 1 do
              let v = mesh.Vertices[vi]

              let w =
                System.Numerics.Vector3.Transform(
                  System.Numerics.Vector3(v.X, v.Y, v.Z),
                  m
                )

              let bin = int((w.X - bounds.MinX) / binWidth)

              if bin >= 0 && bin < bins && w.Y > profile[bin] then
                profile[bin] <- w.Y

          for child in node.Children do
            profileWalk child m

        profileWalk scene.RootNode System.Numerics.Matrix4x4.Identity

        let profileText =
          profile
          |> Array.map(fun y ->
            if y = Single.MinValue then "-" else $"%0.2f{y}")
          |> String.concat " "

        printfn $"  top profile by X (low..high edge): {profileText}"

      0
