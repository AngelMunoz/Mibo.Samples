module BoneProbe.Xnb

// Inspects a CONTENT-PIPELINE Model (built XNB, loaded via a ContentManager)
// the way Mibo.MonoGame's skinned draw paths see it: the ModelBone list the
// palette upload is sized against, and the BLENDINDICES values the mesh parts
// actually carry. Cross-checking those against the raw Assimp skeleton
// (BoneProbe raw/palette) explains skinning artifacts that only hit some
// models: content bones beyond the raw skeleton read the palette's identity
// tail on the per-model path but ZERO rows on the instanced paths.
//
// Usage: dotnet run --project BoneProbe -- xbones <content-root> <asset-name> [raw-bone-count]
//   e.g. xbones Platformer3D/MonoDX12/bin/Debug/net10.0-windows/Content
//            kenney_platformer-kit/Models/character-oozi 6

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics

/// Decodes the BLENDINDICES element bytes for one vertex channel into its
/// per-component bone indices (supports the formats the pipeline emits).
let private decodeIndices(format: VertexElementFormat, bytes: byte[]) : int[] =
  match format with
  | VertexElementFormat.Byte4 -> [|
      int bytes[0]
      int bytes[1]
      int bytes[2]
      int bytes[3]
    |]
  | VertexElementFormat.Short4 -> [|
      int(BitConverter.ToUInt16(bytes, 0))
      int(BitConverter.ToUInt16(bytes, 2))
      int(BitConverter.ToUInt16(bytes, 4))
      int(BitConverter.ToUInt16(bytes, 6))
    |]
  | VertexElementFormat.Short2 -> [|
      int(BitConverter.ToUInt16(bytes, 0))
      int(BitConverter.ToUInt16(bytes, 2))
    |]
  | VertexElementFormat.Single -> [| int(BitConverter.ToSingle(bytes, 0)) |]
  | _ ->
    eprintfn $"  (unhandled BLENDINDICES format: {format})"
    Array.empty

let private dumpModel
  (content: ContentManager)
  (assetName: string)
  (rawBoneCount: int option)
  : int =
  let model =
    try
      content.Load<Model>(assetName)
    with ex ->
      eprintfn $"Failed to load '{assetName}': {ex.Message}"
      eprintfn "(pass the content OUTPUT dir, e.g. <project>/bin/.../Content)"
      null

  if isNull model then
    2
  elif model.Bones.Count = 0 then
    printfn $"model {assetName} has no bones — not a skinned rig"

    1
  else
    printfn
      $"model {assetName} bones={model.Bones.Count} meshes={model.Meshes.Count}"

    for i, name in model.Bones |> Seq.mapi(fun i b -> (i, b.Name)) do
      let rawNote =
        match rawBoneCount with
        | Some raw when i >= raw -> "  <-- beyond raw skeleton palette"
        | _ -> ""

      printfn $"  bone[{i}] = {name}{rawNote}"

    for mesh in model.Meshes do
      printfn
        $"mesh '{mesh.Name}': parent bone = '{mesh.ParentBone.Name}' #{mesh.ParentBone.Index} ({mesh.MeshParts.Count} parts)"

      for pi = 0 to mesh.MeshParts.Count - 1 do
        let part = mesh.MeshParts[pi]

        printfn
          $"  part[{pi}] effect={part.Effect.GetType().Name} voffset={part.VertexOffset} start={part.StartIndex} prims={part.PrimitiveCount} verts={part.NumVertices}"

        let decl = part.VertexBuffer.VertexDeclaration
        let stride = decl.VertexStride

        let declElements =
          decl.GetVertexElements()
          |> Array.map(fun e ->
            $"{e.Offset}:{e.VertexElementFormat}:{e.VertexElementUsage}#{e.UsageIndex}")
          |> String.concat " "

        printfn $"    decl stride={stride}: {declElements}"

        // Scan the part's vertices for BLENDINDICES values: which bones does
        // the content actually reference, and how often.
        let indexElements =
          decl.GetVertexElements()
          |> Array.filter(fun e ->
            e.VertexElementUsage = VertexElementUsage.BlendIndices)

        if indexElements.Length = 0 then
          printfn "    (no BLENDINDICES channel — part is not skinned)"
        else
          let data =
            Array.zeroCreate<byte>(part.VertexBuffer.VertexCount * stride)

          part.VertexBuffer.GetData(data)

          // Also dump TEXCOORD channel ranges: a second UV set (TEXCOORD#1+)
          // collides with the instance stream's TEXCOORD1..4 semantics on
          // MonoGame DX12's first-match input layout matching.
          let texElements =
            decl.GetVertexElements()
            |> Array.filter(fun e ->
              e.VertexElementUsage = VertexElementUsage.TextureCoordinate)

          for e in texElements do
            let mutable minX = System.Single.PositiveInfinity
            let mutable minY = System.Single.PositiveInfinity
            let mutable maxX = System.Single.NegativeInfinity
            let mutable maxY = System.Single.NegativeInfinity

            for v = 0 to part.NumVertices - 1 do
              let base' = (part.VertexOffset + v) * stride + e.Offset
              let x = BitConverter.ToSingle(data, base')
              let y = BitConverter.ToSingle(data, base' + 4)

              minX <- min minX x
              maxX <- max maxX x
              minY <- min minY y
              maxY <- max maxY y

            printfn
              $"    TEXCOORD#{e.UsageIndex} range: x [{minX:G3} .. {maxX:G3}] y [{minY:G3} .. {maxY:G3}]"

          for e in indexElements do
            let histogram = System.Collections.Generic.Dictionary<int, int>()

            for v = 0 to part.NumVertices - 1 do
              let base' = (part.VertexOffset + v) * stride + e.Offset

              let width =
                match e.VertexElementFormat with
                | VertexElementFormat.Byte4
                | VertexElementFormat.Short4 -> 8
                | VertexElementFormat.Short2 -> 4
                | _ -> 4

              let bytes = Array.sub data base' width

              for idx in decodeIndices(e.VertexElementFormat, bytes) do
                histogram[idx] <-
                  (histogram.TryGetValue idx
                   |> fun (ok, n) -> if ok then n + 1 else 1)

            let sorted =
              histogram
              |> Seq.map(fun (KeyValue(k, v)) -> struct (k, v))
              |> Array.ofSeq
              |> Array.sortBy(fun struct (k, _) -> k)

            let usage =
              sorted
              |> Array.map(fun struct (k, n) ->
                let flag =
                  match rawBoneCount with
                  | Some raw when k >= raw -> "!"
                  | _ -> ""

                $"{k}{flag}x{n}")
              |> String.concat ", "

            printfn
              $"    BLENDINDICES#{e.UsageIndex} ({e.VertexElementFormat}): {usage}"

    0

/// The dump runs inside a short-lived Game: DesktopGL can't build a
/// GraphicsDevice without a window-backed adapter (CurrentDisplayMode NREs).
type private ProbeGame
  (
    contentRoot: string,
    assetName: string,
    rawBoneCount: int option,
    code: int ref
  ) as this =
  inherit Game()

  // Registers the graphics device service; the device is created in Initialize.
  let gdm = new GraphicsDeviceManager(this)

  override this.LoadContent() =
    this.Content.RootDirectory <- contentRoot
    code.Value <- dumpModel this.Content assetName rawBoneCount
    this.Exit()

let probe
  (contentRoot: string)
  (assetName: string)
  (rawBoneCount: int option)
  : int =
  let code = ref 0
  let game = new ProbeGame(contentRoot, assetName, rawBoneCount, code)
  game.Run()
  code.Value
