namespace Defli.Raylib

open System
open System.IO
open System.Numerics
open FSharp.NativeInterop
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli.State
open Defli.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// AuraView — procedural glow for the boss aura and the tower
// range ring. One GLSL shader (assets/shaders/aura.*), loaded
// lazily on first draw (the GL context exists by then; loading at
// construction would fail).
//
// Each aura is ONE self-contained DrawImmediate command — the
// documented escape hatch (docs/graphics2d/custom-commands.md):
// the renderer flushes the batch and exits any active camera and
// shader modes, the action runs raw backend calls, then the
// previous modes are restored. The action re-opens the DRAWN
// camera (same neutral snapshot the world pass used), opens the
// shader mode, uploads the per-aura uniforms and draws the disc.
// Per-aura uniforms stay paired with their disc because everything
// happens inside a single command — a beginShader/endShader scope
// cannot carry per-draw uniforms through the deferred stream (a
// DrawImmediate between them just exits the shader mode again).
// Fallback: the old circle outline when the shader is unavailable.
// ─────────────────────────────────────────────────────────────

// raylib-cs uses DisableRuntimeMarshalling: scalar/vector uniforms MUST
// go through fixed + NativePtr.toVoidPtr (see Mibo/AGENTS.md). Plain
// module functions — no class binding.
module AuraUniforms =

  let inline setFloat (s: Shader) (loc: int) (value: float32) =
    if loc >= 0 then
      use p = fixed &value

      Raylib.SetShaderValue(
        s,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Float
      )

  let inline setVec2 (s: Shader) (loc: int) (value: Vector2) =
    if loc >= 0 then
      use p = fixed &value

      Raylib.SetShaderValue(
        s,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Vec2
      )

  let inline setVec4 (s: Shader) (loc: int) (value: Vector4) =
    if loc >= 0 then
      use p = fixed &value

      Raylib.SetShaderValue(
        s,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Vec4
      )

[<Sealed>]
type AuraView() =

  // Shader + cached uniform locations, resolved once on first draw.
  let mutable shader = ValueNone
  let mutable locCenter = 0
  let mutable locRadius = 0
  let mutable locTime = 0
  let mutable locColor = 0
  let mutable locRing = 0

  let ensureShader() =
    match shader with
    | ValueSome _ -> ()
    | ValueNone ->
      let vsPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "aura.vs")

      let fragPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "aura.frag")

      if File.Exists vsPath && File.Exists fragPath then
        let s = Raylib.LoadShader(vsPath, fragPath)

        if s.Id <> 0u then
          shader <- ValueSome s
          locCenter <- Raylib.GetShaderLocation(s, "auraCenter")
          locRadius <- Raylib.GetShaderLocation(s, "auraRadius")
          locTime <- Raylib.GetShaderLocation(s, "auraTime")
          locColor <- Raylib.GetShaderLocation(s, "auraColor")
          locRing <- Raylib.GetShaderLocation(s, "auraRing")

  /// Draws one shader aura: a soft radial glow with a bright band at
  /// `ringPos` (0..1 of the radius), additively blended. `time` is the
  /// frame's total seconds (drives the pulse/shimmer). `camera` is the
  /// frame's neutral camera snapshot and `viewport` the window size —
  /// the action rebuilds the exact camera the world pass drew with.
  member _.Draw
    (camera: CameraState)
    (viewport: Vector2)
    (center: Vector2)
    (radius: float32)
    (color: Mibo.Color)
    (ringPos: float32)
    (time: float32)
    (buffer: RenderBuffer2D)
    =
    ensureShader()

    match shader with
    | ValueNone ->
      buffer.circleOutline(center, radius, color, layer = Layers.Effects).drop()

    | ValueSome s ->
      let rgba = Color.toVector4 color

      buffer
        .drawImmediate(
          (fun () ->
            let cam = CameraView.toRaylib camera viewport

            Raylib.BeginMode2D(cam)
            Raylib.BeginBlendMode(BlendMode.Additive)
            Raylib.BeginShaderMode(s)
            AuraUniforms.setVec2 s locCenter center
            AuraUniforms.setFloat s locRadius radius
            AuraUniforms.setFloat s locTime time
            AuraUniforms.setVec4 s locColor rgba
            AuraUniforms.setFloat s locRing ringPos

            Raylib.DrawCircleV(
              center,
              radius,
              RaylibColor.toRaylibColor Mibo.Color.White
            )

            Raylib.EndShaderMode()
            Raylib.EndBlendMode()
            Raylib.EndMode2D()),
          layer = Layers.Effects
        )
        .drop()
