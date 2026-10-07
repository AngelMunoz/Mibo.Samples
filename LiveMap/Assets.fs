module LiveMap.Assets

#nowarn "9"

open System.Collections.Frozen
open System.Collections.Generic
open FSharp.NativeInterop
open LiveMap
open LiveMap.Catalog
open LiveMap.Semantics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Raylib_cs

/// One model, prepared for drawing: the raylib model plus the mesh and
/// material of each of its parts. Reading the native arrays once at load
/// keeps the draw path out of pointer arithmetic.
type LoadedModel = {
  Model: Model
  Parts: struct (Mesh * Material3D)[]
}

/// Everything LiveMap loads, resolved once at startup.
type Assets = {
  /// The tile sheet every flat word paints from.
  Sheet: Texture2D
  Font: Font
  /// Every model the block palette can name, by model name.
  Models: FrozenDictionary<string, LoadedModel>
}

let private loadModel (service: IAssets) (info: ModelInfo) : LoadedModel =
  // raylib loads by extension; the catalog stores paths without one
  let model = service.Model(info.Path + ".glb")

  let parts =
    Array.init model.MeshCount (fun index ->
      let mesh = NativePtr.get model.Meshes index
      let materialIndex = NativePtr.get model.MeshMaterial index
      let material = NativePtr.get model.Materials materialIndex
      struct (mesh, Material3D.fromRaylibMaterial material))

  { Model = model; Parts = parts }

/// Loads the sheet, the font, and every model the block palette names.
///
/// The block words are the only models the sample can draw, so loading
/// them all up front bounds the work and leaves the draw path with
/// dictionary reads.
let load(ctx: GameContext) : Assets =
  let service = GameContext.getService<IAssets> ctx

  // the sheet has no gutter between tiles, so point sampling keeps the
  // cell edges crisp at any zoom
  let sheet =
    service.Texture("assets/" + Tiles.SheetPath)
    |> Texture.filter TextureFilter.Point

  let font = service.Font "assets/Fonts/monogram.ttf"

  let names =
    Blocks.words
    |> Array.map(fun (_, cell) -> cell.Model.Name)
    |> Array.distinct

  let models = Dictionary<string, LoadedModel>(names.Length)

  for name in names do
    match Models.tryByName name with
    | ValueSome info -> models[name] <- loadModel service info
    | ValueNone -> failwith $"the model catalog has no '{name}'"

  {
    Sheet = sheet
    Font = font
    Models = models.ToFrozenDictionary()
  }
