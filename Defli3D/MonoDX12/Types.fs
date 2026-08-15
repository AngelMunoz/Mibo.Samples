namespace Defli3D.MonoGame

open System
open System.Collections.Generic
open System.IO
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Types — view-edge types and shared presentation state for the
// MonoGame clients. Everything here is PRESENTATION state (asset
// caches, scratch buffers, the sim clock); the draw contract (views
// read only the packed RenderFrame + GameContext) is unaffected.
// Mirrors Defli/MonoDX12/Types.fs in role.
// ─────────────────────────────────────────────────────────────

/// The 2D HUD pass's render layers (the 3D pass has no layers).
module Layers =

  [<Literal>]
  let Hud = 10<Mibo.Elmish.Graphics2D.RenderLayer>

/// XNB asset names for the MonoGame content pipeline — no extension,
/// resolved through IAssets (ContentManager) relative to the Content
/// output dir. The .mgcb (MonoDX12/Content/Content.mgcb) names its
/// assets to mirror these paths.
module Paths =

  [<Literal>]
  let Font = "Fonts/Monogram"

  /// The model dataset (Shared/State/Models.fs) IS the content-name
  /// table: ModelInfo.Path ("kenney_tower_defense_kit/Models/<name>")
  /// is already the .mgcb asset name — no path mapping needed. The
  /// views key their mesh caches directly on ModelInfo.Path.
  let inline modelName(info: ModelInfo) = info.Path

/// The sim clock for draw-side animation (hover bob, idle spins).
/// The renderers don't receive GameTime, so the observer (Program.fs)
/// records the game time here each step; the views read Time.now().
module Time =

  let mutable private seconds = 0.0

  let set(t: double) : unit = seconds <- t
  let now() : float32 = float32 seconds

/// A shared 1×1 white texture for billboard work (health bars, VFX
/// quads). Created lazily on the first frame that needs it — the
/// GraphicsDevice only exists after the game initializes.
module WhiteTex =

  let mutable private tex: Texture2D voption = ValueNone

  let get(gd: GraphicsDevice) : Texture2D =
    match tex with
    | ValueSome t -> t
    | ValueNone ->
      let t = new Texture2D(gd, 1, 1, false, SurfaceFormat.Color)
      t.SetData([| Microsoft.Xna.Framework.Color.White |])
      tex <- ValueSome t
      t

/// Content-pipeline model resolution. MonoGame stores content-pipeline
/// vertices in bone-local space, not model-root space: the instanced
/// path grabs raw vertex buffers, so each model's FIRST mesh's
/// absolute bone transform (CopyAbsoluteBoneTransformsTo) must be
/// folded into the instance world transforms — see the comment at
/// Platformer3D/MonoDX12/View.fs:31-38. Meshes wrap as PrimitiveMesh +
/// Material3D (fromModelMeshPart) once, cached forever.
module ModelCache =

  let mutable private currentContext: GameContext voption = ValueNone

  let private meshMaterial =
    Dictionary<string, struct (PrimitiveMesh * Material3D)[]>()

  // Public: referenced by the inline accessors (FS1113 — inline
  // bodies can only touch sufficiently accessible members).
  let boneTransforms = Dictionary<string, Matrix>()

  /// Per-PART absolute bone transforms, parallel to resolve's array:
  /// multi-mesh models (the weapons: base+barrel, body+arrow) have
  /// parts under DIFFERENT bones — each part must fold its own.
  let private partBones = Dictionary<string, Matrix[]>()

  /// Generous fixed bounds — content models vary in size; the shadow
  /// pass frustum-culls caster meshes with these, so a loose sphere
  /// only over-draws, never under-culls.
  let private bounds = BoundingSphere(Vector3.Zero, 2.5f)

  /// Sets the per-frame GameContext used for lazy asset loads. The
  /// views call this at the top of the frame, before any resolve.
  let setContext(ctx: GameContext) : unit = currentContext <- ValueSome ctx

  /// Builds a SELF-CONTAINED PrimitiveMesh for one ModelMeshPart. The
  /// content pipeline packs the whole model into shared vertex/index
  /// buffers — a part's geometry lives at [VertexOffset, +NumVertices)
  /// vertices and [StartIndex, +PrimitiveCount·3) indices, and the
  /// part's indices are LOCAL to its own vertex slice (stock
  /// ModelMesh.Draw passes VertexOffset as baseVertex). PrimitiveMesh
  /// draws from zero, so multi-mesh models (the weapons) need each
  /// part sliced into its own buffers — verbatim indices, no rebase.
  let private slicePart
    (gd: GraphicsDevice)
    (part: ModelMeshPart)
    : PrimitiveMesh =
    let decl = part.VertexBuffer.VertexDeclaration
    let stride = decl.VertexStride
    let indexCount = part.PrimitiveCount * 3

    let vb = new VertexBuffer(gd, decl, part.NumVertices, BufferUsage.WriteOnly)

    let vBytes = Array.zeroCreate<byte>(part.NumVertices * stride)

    part.VertexBuffer.GetData<byte>(
      part.VertexOffset * stride,
      vBytes,
      0,
      vBytes.Length
    )

    vb.SetData<byte>(vBytes)

    let ib =
      new IndexBuffer(
        gd,
        part.IndexBuffer.IndexElementSize,
        indexCount,
        BufferUsage.WriteOnly
      )

    if part.IndexBuffer.IndexElementSize = IndexElementSize.ThirtyTwoBits then
      let indices = Array.zeroCreate<int> indexCount

      part.IndexBuffer.GetData<int>(part.StartIndex * 4, indices, 0, indexCount)

      ib.SetData<int>(indices)
    else
      let indices = Array.zeroCreate<uint16> indexCount

      part.IndexBuffer.GetData<uint16>(
        part.StartIndex * 2,
        indices,
        0,
        indexCount
      )

      ib.SetData<uint16>(indices)

    {
      Vertices = vb
      Indices = ib
      PrimitiveCount = part.PrimitiveCount
      Bounds = bounds
    }

  /// The raw .glb for a content name — the pipeline's source file.
  /// The content name mirrors the repo-relative asset path, so probe
  /// the usual working directories (repo root, project dirs, the
  /// content root's parents) for "assets/<name>.glb".
  let private resolveRawGlb(name: string) : string voption =
    [|
      $"assets/{name}.glb"
      $"../assets/{name}.glb"
      $"../../assets/{name}.glb"
      $"../../../assets/{name}.glb"
      Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "..",
        "assets",
        $"{name}.glb"
      )
    |]
    |> Array.tryFind File.Exists
    |> ValueOption.ofOption

  /// The import flags Mibo's own raw-scene path uses (AssetsService.
  /// loadScene) — same post-processing keeps UV/winding conventions
  /// aligned with the pipeline-built assets.
  let private importFlags =
    Assimp.PostProcessSteps.FindDegenerates
    ||| Assimp.PostProcessSteps.FindInvalidData
    ||| Assimp.PostProcessSteps.FlipUVs
    ||| Assimp.PostProcessSteps.FlipWindingOrder
    ||| Assimp.PostProcessSteps.JoinIdenticalVertices
    ||| Assimp.PostProcessSteps.ImproveCacheLocality
    ||| Assimp.PostProcessSteps.OptimizeMeshes
    ||| Assimp.PostProcessSteps.Triangulate

  /// AssimpNetter exposes node transforms as System.Numerics.
  /// Matrix4x4; Mibo's sanctioned conversion to the XNA convention is
  /// the implicit operator (Conversions.fromNumericsMatrix).
  let private nodeWorld (parent: Matrix) (node: Assimp.Node) : Matrix =
    parent * Microsoft.Xna.Framework.Matrix.op_Implicit(node.Transform)

  /// Builds self-contained parts from an imported Assimp scene:
  /// every mesh flattened into MODEL space (its node's world
  /// transform baked into the vertices — identity bones), one
  /// VertexBuffer/IndexBuffer pair per mesh. Used by the
  /// content-pipeline repair below.
  let private rawSceneParts
    (gd: GraphicsDevice)
    (scene: Assimp.Scene)
    (material: Material3D)
    : struct (PrimitiveMesh * Material3D)[] voption =
    if isNull scene || isNull scene.RootNode then
      ValueNone
    else
      let flattened = ResizeArray<struct (Assimp.Mesh * Matrix)>()

      let rec walk (node: Assimp.Node) (parent: Matrix) =
        let world = nodeWorld parent node

        for i in node.MeshIndices do
          flattened.Add(struct (scene.Meshes[i], world))

        for child in node.Children do
          walk child world

      walk scene.RootNode Matrix.Identity

      let parts = ResizeArray<struct (PrimitiveMesh * Material3D)>()

      for struct (mesh, world) in flattened do
        if mesh.VertexCount > 0 && mesh.FaceCount > 0 then
          let hasNormals = mesh.Normals.Count >= mesh.VertexCount

          let hasUv =
            mesh.TextureCoordinateChannelCount > 0
            && mesh.TextureCoordinateChannels[0].Count >= mesh.VertexCount

          let verts =
            Array.zeroCreate<VertexPositionNormalTexture> mesh.VertexCount

          for i = 0 to mesh.VertexCount - 1 do
            let p = mesh.Vertices[i]

            let pos = Vector3.Transform(Vector3(p.X, p.Y, p.Z), world)

            let nrm =
              if hasNormals then
                let n = mesh.Normals[i]

                Vector3.Normalize(
                  Vector3.TransformNormal(Vector3(n.X, n.Y, n.Z), world)
                )
              else
                Vector3.Up

            let uv =
              if hasUv then
                let u = mesh.TextureCoordinateChannels[0][i]
                Vector2(u.X, u.Y)
              else
                Vector2.Zero

            verts[i] <- VertexPositionNormalTexture(pos, nrm, uv)

          let indices = mesh.GetIndices() |> Seq.toArray

          if indices.Length >= 3 then
            let vb =
              new VertexBuffer(
                gd,
                VertexPositionNormalTexture.VertexDeclaration,
                mesh.VertexCount,
                BufferUsage.WriteOnly
              )

            vb.SetData(verts)

            // Kit meshes are far below 65 k vertices — 16-bit indices.
            let shorts = indices |> Array.map uint16

            let ib =
              new IndexBuffer(
                gd,
                IndexElementSize.SixteenBits,
                shorts.Length,
                BufferUsage.WriteOnly
              )

            ib.SetData(shorts)

            parts.Add(
              struct ({
                        Vertices = vb
                        Indices = ib
                        PrimitiveCount = shorts.Length / 3
                        Bounds = bounds
                      },
                      material)
            )

      if parts.Count = 0 then
        ValueNone
      else
        ValueSome(parts.ToArray())

  /// Resolves the (PrimitiveMesh × Material3D) parts for a content
  /// model name (ModelInfo.Path). Cached: the per-frame hot path is
  /// one dictionary hit per model.
  let resolve(name: string) : struct (PrimitiveMesh * Material3D)[] =
    match meshMaterial |> Dictionary.tryGetValue name with
    | ValueSome cached -> cached
    | ValueNone ->
      let ctx =
        match currentContext with
        | ValueSome c -> c
        | ValueNone ->
          failwith $"ModelCache.resolve called before the first frame ({name})"

      let assets = GameContext.getService<IAssets> ctx
      let m = assets.Model name

      let absolute =
        if m.Bones.Count > 0 then
          let a = Array.zeroCreate<Matrix> m.Bones.Count
          m.CopyAbsoluteBoneTransformsTo a
          boneTransforms[name] <- a[m.Meshes[0].ParentBone.Index]
          a
        else
          Unchecked.defaultof<Matrix[]>

      let parts = ResizeArray<struct (PrimitiveMesh * Material3D)>()
      let bones = ResizeArray<Matrix>()

      // Slicing needs the device (each part gets its own GPU buffers).
      let gd = MonoGameGameContext.getGraphicsDevice ctx

      for mesh in m.Meshes do
        // Each mesh folds ITS OWN parent bone — the content pipeline
        // stores vertices in bone-local space, and a model's meshes
        // need not share one.
        let bone =
          if absolute <> null then
            absolute[mesh.ParentBone.Index]
          else
            Matrix.Identity

        for part in mesh.MeshParts do
          parts.Add(
            struct (slicePart gd part,
                    {
                      Material3D.fromModelMeshPart part with
                          Roughness = 0.65f
                          Metallic = 0.2f
                    })
          )

          bones.Add bone

      let result = parts.ToArray()
      partBones[name] <- bones.ToArray()

      // ── Content-pipeline repair ────────────────────────────────
      // The FbxImporter drops the ROOT node's own mesh when the root
      // also has mesh children — the kit's weapons (base+barrel,
      // body+arrow) import as ONE part with half the triangles. When
      // the XNB under-reports against the source GLB, rebuild the
      // parts from the RAW file via Assimp (per-node world transforms
      // flattened into model space — every part's bone is identity).
      // Materials come from the surviving XNB part (the kit's shared
      // colormap texture).
      let xnbTris =
        let mutable t = 0

        for struct (mesh, _) in result do
          t <- t + mesh.PrimitiveCount

        t

      let final =
        match resolveRawGlb name with
        | ValueNone -> result
        | ValueSome path ->
          use importer = new Assimp.AssimpContext()
          let scene = importer.ImportFile(path, importFlags)

          let sceneTris =
            if isNull scene then
              0
            else
              (let mutable t = 0

               for mesh in scene.Meshes do
                 t <- t + mesh.FaceCount

               t)

          if sceneTris > xnbTris then
            let material =
              if result.Length > 0 then
                let struct (_, mat) = result[0]
                mat
              else
                Material3D.defaults

            match rawSceneParts gd scene material with
            | ValueSome raw ->
              partBones[name] <-
                Array.init raw.Length (fun _ -> Matrix.Identity)

              raw
            | ValueNone -> result
          else
            result

      meshMaterial[name] <- final
      final

  /// Per-part bone transforms parallel to resolve's array (one
  /// identity when unknown) — the entity batcher folds each part's
  /// own bone into the instance transforms. Public for the inline
  /// accessor below (FS1113).
  let partBoneCache = partBones

  let inline bonesOf(name: string) : Matrix[] =
    match partBoneCache |> Dictionary.tryGetValue name with
    | ValueSome bones -> bones
    | ValueNone -> [| Matrix.Identity |]

  /// The baked absolute bone transform for a model (identity when the
  /// model has no bones or hasn't resolved yet). The batcher's Draw
  /// folds this in AFTER resolve, so a not-yet-loaded model can never
  /// render un-boned.
  let inline boneOf(name: string) : Matrix =
    match boneTransforms |> Dictionary.tryGetValue name with
    | ValueSome bone -> bone
    | ValueNone -> Matrix.Identity

  /// Warms the cache for every name (avoids mid-frame Content.Load
  /// stalls when a model first appears).
  let inline warm(names: string[]) : unit =
    for name in names do
      resolve name |> ignore

/// Grow-only per-model-name instance scratch for the entity views
/// (ModelProbe idiom — one transform array per model kind, refilled
/// every frame; steady state allocates nothing). NOT a batcher: the
/// views own the per-frame fill and the draw timing; this module
/// only owns the arrays. Each view resets → fills → draws at its own
/// point in the pass (a view's reset clears every group, so the
/// groups of views that already drew are gone; the last draw after
/// the final view emits only that view's groups). The render buffer
/// copies the transforms into pooled arrays at record time, so
/// refilling one scratch per kind per frame is safe.
/// Tinted groups (selection rings) keep a parallel per-instance
/// color array; callers tint consistently per model name (a group
/// is either all-tinted or untinted).
module InstanceScratch =

  let private transforms = Dictionary<string, Matrix[]>()
  let private tints = Dictionary<string, Microsoft.Xna.Framework.Color[]>()
  /// Per-name bone-folded copies (draw writes here, never into the
  /// raw transforms — parts would accumulate each other's bones).
  let private folded = Dictionary<string, Matrix[]>()
  let private counts = Dictionary<string, int>()

  /// Clears every group's count (arrays keep their storage).
  let reset() : unit = counts.Clear()

  let addCore
    (name: string)
    (transform: Matrix)
    (tint: Microsoft.Xna.Framework.Color voption)
    : unit =
    match counts |> Dictionary.tryGetValue name with
    | ValueSome n ->
      let arr = transforms[name]

      if arr.Length <= n then
        let bigger = Array.zeroCreate<Matrix>(max (arr.Length * 2) 32)
        System.Array.Copy(arr, bigger, n)
        transforms[name] <- bigger

      transforms[name][n] <- transform

      match tint with
      | ValueSome c ->
        let ta = tints[name]

        if ta.Length <= n then
          let bigger =
            Array.zeroCreate<Microsoft.Xna.Framework.Color>(
              max (ta.Length * 2) 32
            )

          System.Array.Copy(ta, bigger, n)
          tints[name] <- bigger

        tints[name][n] <- c
      | ValueNone -> ()

      counts[name] <- n + 1
    | ValueNone ->
      let arr = Array.zeroCreate<Matrix> 32
      arr[0] <- transform
      transforms[name] <- arr

      match tint with
      | ValueSome c ->
        let ta = Array.zeroCreate<Microsoft.Xna.Framework.Color> 32
        ta[0] <- c
        tints[name] <- ta
      | ValueNone -> ()

      counts[name] <- 1

  /// Appends one untinted instance transform.
  let inline add (name: string) (transform: Matrix) : unit =
    addCore name transform ValueNone

  /// Appends a per-instance tinted instance (MonoGame instanced draws
  /// support per-instance colors — albedo × color.rgb, alpha ×
  /// color.a, which also routes the draw through the translucent pass).
  let inline addTinted
    (name: string)
    (transform: Matrix)
    (color: Microsoft.Xna.Framework.Color)
    : unit =
    addCore name transform (ValueSome color)

  /// Emits one .instanced draw per sub-mesh per group. Each part's
  /// OWN absolute bone transform (bonesOf — multi-mesh models have
  /// parts under different bones) is folded into a per-name scratch
  /// copy of the transforms — the raw scratch is never mutated, so
  /// parts never accumulate each other's bones.
  let draw(buffer: RenderBuffer3D) : unit =
    for KeyValueV(name, n) in counts do
      if n > 0 then
        // Resolve FIRST so the bone cache is filled: a model that
        // first appears this frame (not in the warm set) never draws
        // a frame un-boned.
        let parts = ModelCache.resolve name
        let bones = ModelCache.bonesOf name
        let arr = transforms[name]

        let fold =
          match folded |> Dictionary.tryGetValue name with
          | ValueSome f when f.Length >= n -> f
          | _ ->
            let f = Array.zeroCreate<Matrix>(max n 32)
            folded[name] <- f
            f

        let tintArr = tints |> Dictionary.tryGetValue name

        for i = 0 to parts.Length - 1 do
          let struct (mesh, material) = parts[i]
          let bone = if i < bones.Length then bones[i] else Matrix.Identity

          if bone <> Matrix.Identity then
            for j = 0 to n - 1 do
              fold[j] <- bone * arr[j]
          else
            for j = 0 to n - 1 do
              fold[j] <- arr[j]

          match tintArr with
          | ValueSome tintArr ->
            buffer.instanced(mesh, fold, material, n, colors = tintArr).drop()
          | ValueNone -> buffer.instanced(mesh, fold, material, n).drop()
