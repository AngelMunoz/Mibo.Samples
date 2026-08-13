namespace Defli3D.MonoGame

open System.Collections.Generic
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

  // Public: referenced by the inline boneOf (FS1113 — inline bodies
  // can only touch sufficiently accessible members).
  let boneTransforms = Dictionary<string, Matrix>()

  /// Generous fixed bounds — content models vary in size; the shadow
  /// pass frustum-culls caster meshes with these, so a loose sphere
  /// only over-draws, never under-culls.
  let private bounds = BoundingSphere(Vector3.Zero, 2.5f)

  /// Sets the per-frame GameContext used for lazy asset loads. The
  /// views call this at the top of the frame, before any resolve.
  let setContext(ctx: GameContext) : unit = currentContext <- ValueSome ctx

  let private wrapPartAsPrimitive(part: ModelMeshPart) : PrimitiveMesh = {
    Vertices = part.VertexBuffer
    Indices = part.IndexBuffer
    PrimitiveCount = part.PrimitiveCount
    Bounds = bounds
  }

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

      if m.Meshes.Count > 0 && m.Bones.Count > 0 then
        let absolute = Array.zeroCreate<Matrix> m.Bones.Count
        m.CopyAbsoluteBoneTransformsTo absolute
        boneTransforms[name] <- absolute[m.Meshes[0].ParentBone.Index]

      let result =
        if m.Meshes.Count > 0 then
          [|
            for mesh in m.Meshes do
              for part in mesh.MeshParts do
                struct (wrapPartAsPrimitive part,
                        {
                          Material3D.fromModelMeshPart part with
                              Roughness = 0.65f
                              Metallic = 0.2f
                        })
          |]
        else
          Array.empty

      meshMaterial[name] <- result
      result

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

  /// Emits one .instanced draw per sub-mesh per group. The absolute
  /// bone transform is folded into the instance matrices here — after
  /// ModelCache.resolve filled the bone cache — so an unresolved
  /// model can never draw un-boned.
  let draw(buffer: RenderBuffer3D) : unit =
    for KeyValueV(name, n) in counts do
      if n > 0 then
        // Resolve FIRST so the bone cache is filled: boneOf reads the
        // absolute root-bone transform resolve just baked, so a model
        // that first appears this frame (not in the warm set) never
        // draws a frame un-boned.
        let parts = ModelCache.resolve name
        let bone = ModelCache.boneOf name
        let arr = transforms[name]

        if bone <> Matrix.Identity then
          for j = 0 to n - 1 do
            arr[j] <- bone * arr[j]

        match tints |> Dictionary.tryGetValue name with
        | ValueSome tintArr ->
          for struct (mesh, material) in parts do
            buffer.instanced(mesh, arr, material, n, colors = tintArr).drop()
        | ValueNone ->
          for struct (mesh, material) in parts do
            buffer.instanced(mesh, arr, material, n).drop()
