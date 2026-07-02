module BoneProbe.Scene

open Assimp

let postProcessFlags =
  PostProcessSteps.FindDegenerates
  ||| PostProcessSteps.FindInvalidData
  ||| PostProcessSteps.FlipUVs
  ||| PostProcessSteps.FlipWindingOrder
  ||| PostProcessSteps.JoinIdenticalVertices
  ||| PostProcessSteps.ImproveCacheLocality
  ||| PostProcessSteps.OptimizeMeshes
  ||| PostProcessSteps.Triangulate

/// Attempt to load a scene. Returns None on any failure (no File.Exists precheck).
/// Surfaces the exception type and message to stderr so the diagnostic tool
/// distinguishes missing-file vs. corrupt-glb vs. native-load failures.
let tryLoad(path: string) : Scene voption =
  try
    use importer = new AssimpContext()
    let scene = importer.ImportFile(path, postProcessFlags)
    if isNull scene then ValueNone else ValueSome scene
  with ex ->
    eprintfn $"assimp import failed: {ex.GetType().Name}: {ex.Message}"
    ValueNone

type Mode =
  | Raw
  | Palette

type Verbosity =
  | Full
  | Summary

type Options = {
  Mode: Mode
  Path: string
  Verbosity: Verbosity
  Focus: string option
}
