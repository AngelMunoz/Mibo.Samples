namespace Defli3D.MonoGame

open System.Collections.Generic
open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Types — view-edge types and shared presentation state for the
// MonoGame clients. Everything here is PRESENTATION state (asset
// caches, scratch buffers); the draw contract (views read only the
// packed RenderFrame + GameContext — the frame carries the sim
// clock as frame.Time) is unaffected. Mirrors Defli/MonoDX12/
// Types.fs in role.
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
  /// views key their part caches directly on ModelInfo.Path.
  let inline modelName(info: ModelInfo) = info.Path

/// Content-pipeline model resolution. The framework's ModelParts.ofModel
/// wraps every mesh part as a ZERO-COPY slice of the model's shared
/// buffers (cached per Model instance — the ContentManager hands back
/// the same Model per name); this cache layers the name → parts lookup
/// plus the sample's material tuning on top, resolved once per name and
/// cached forever. Draw the parts with meshSlice/instancedSlice passing
/// the part's VertexOffset/StartIndex, folding part.Bone in front of
/// every instance transform (content vertices are bone-local).
module ModelCache =

  let mutable private currentContext: GameContext voption = ValueNone

  let private resolved = Dictionary<string, ModelPart[]>()

  /// Sets the per-frame GameContext used for lazy asset loads. The
  /// views call this at the top of the frame, before any resolve.
  let setContext(ctx: GameContext) : unit = currentContext <- ValueSome ctx

  /// The zero-copy parts of a content model name (ModelInfo.Path), with
  /// the kit's material tuning applied (mid roughness, faintly metallic
  /// — the colormap reads matte on the PBR path). Cached: the per-frame
  /// hot path is one dictionary hit per model.
  let resolve(name: string) : ModelPart[] =
    match resolved |> Dictionary.tryGetValue name with
    | ValueSome cached -> cached
    | ValueNone ->
      let ctx =
        match currentContext with
        | ValueSome c -> c
        | ValueNone ->
          failwith $"ModelCache.resolve called before the first frame ({name})"

      let assets = GameContext.getService<IAssets> ctx

      let parts =
        ModelParts.ofModel(assets.Model name)
        |> Array.map(fun p -> {
          p with
              Material = {
                p.Material with
                    Roughness = 0.65f
                    Metallic = 0.2f
              }
        })

      resolved[name] <- parts
      parts

  /// Warms the cache for every name (avoids mid-frame Content.Load
  /// stalls when a model first appears).
  let warm(names: string[]) : unit =
    for name in names do
      resolve name |> ignore

/// Grow-only per-model-name instance groups owned by ONE view: fill with
/// Add/AddTinted, then Draw once. Each view owns its groups, so there is
/// no cross-view reset protocol to keep in your head (the old shared
/// InstanceScratch needed reset → fill → draw in exactly that order per
/// view — a view that forgot the reset re-drew the previous view's
/// instances). Steady state allocates nothing: the arrays grow only when
/// a frame needs more room, and the render buffer copies the transforms
/// into pooled arrays at record time, so the scratch is safely refilled
/// the next frame.
/// Draw emits one instancedSlice per part per group, with the part's real
/// buffer offsets and its own absolute bone folded into a scratch copy —
/// the raw transforms are never mutated, so parts never accumulate each
/// other's bones. Tinted groups keep a parallel per-instance color array
/// (MonoGame instanced draws support per-instance colors — albedo ×
/// color.rgb, alpha × color.a, which also routes the draw through the
/// translucent pass); callers tint consistently per model name (a group
/// is either all-tinted or untinted).
type InstanceGroups() =

  let transforms = Dictionary<string, Matrix[]>()
  let tints = Dictionary<string, Microsoft.Xna.Framework.Color[]>()
  /// Per-name bone-folded copies (draw writes here, never into the
  /// raw transforms — parts would accumulate each other's bones).
  let folded = Dictionary<string, Matrix[]>()
  let counts = Dictionary<string, int>()

  /// Clears every group's count (arrays keep their storage).
  member _.Clear() : unit = counts.Clear()

  member private _.Append
    (
      name: string,
      transform: Matrix,
      tint: Microsoft.Xna.Framework.Color voption
    ) : unit =
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
  member this.Add(name: string, transform: Matrix) : unit =
    this.Append(name, transform, ValueNone)

  /// Appends a per-instance tinted instance.
  member this.AddTinted
    (name: string, transform: Matrix, color: Microsoft.Xna.Framework.Color)
    : unit =
    this.Append(name, transform, ValueSome color)

  /// Emits one instanced draw per part per group.
  member _.Draw(buffer: RenderBuffer3D) : unit =
    for KeyValueV(name, n) in counts do
      if n > 0 then
        let parts = ModelCache.resolve name
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
          let part = parts[i]

          if part.Bone <> Matrix.Identity then
            for j = 0 to n - 1 do
              fold[j] <- part.Bone * arr[j]
          else
            for j = 0 to n - 1 do
              fold[j] <- arr[j]

          match tintArr with
          | ValueSome tintArr ->
            buffer
              .instancedSlice(
                part.Mesh,
                fold,
                part.Material,
                n,
                colors = tintArr,
                vertexOffset = part.VertexOffset,
                startIndex = part.StartIndex
              )
              .drop()
          | ValueNone ->
            buffer
              .instancedSlice(
                part.Mesh,
                fold,
                part.Material,
                n,
                vertexOffset = part.VertexOffset,
                startIndex = part.StartIndex
              )
              .drop()
