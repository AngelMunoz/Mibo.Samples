namespace FPSSample

open System
open System.Numerics
open Mibo.Layout

/// Prebaked render batches for a level footprint: one transform array
/// per model path, one instance per stack level at the same world
/// center the retired voxel grid produced. Models draw at their
/// native size — no scaling, translate-only transforms, exactly what
/// the retired per-cell context emitted. The backend views pass their
/// native translation constructor.
module LevelBake =

  /// One draw group: the model-path key plus one transform per solid
  /// stack level of the footprint.
  type Group<'M> = { Path: string; Transforms: 'M[] }

  /// Bakes the footprint into per-path instance groups.
  let bake (translate: Vector3 -> 'M) (level: Level.LevelData) : Group<'M>[] =
    let groups = Collections.Generic.Dictionary<string, ResizeArray<'M>>()

    level.Grid
    |> CellGrid2D.iter(fun x z column ->
      for y = 0 to column.Height - 1 do
        let transform = translate(Level.LevelData.cellCenter x y z level)
        let path = Level.Cell.modelPath column.Kind

        match groups.TryGetValue path with
        | true, list -> list.Add transform
        | _ -> groups[path] <- ResizeArray([ transform ]))

    groups
    |> Seq.map(fun kvp -> {
      Path = kvp.Key
      Transforms = kvp.Value.ToArray()
    })
    |> Array.ofSeq
