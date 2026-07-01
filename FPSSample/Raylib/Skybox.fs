namespace FPSSample.Raylib

#nowarn "9"

open System
open System.Diagnostics
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

/// Procedural starry skybox. Renders a sphere from the inside with a custom
/// shader that generates multi-layer twinkling stars + nebula clouds.
type SkyboxModel = {
  Shader: Shader
  Material: Material
  Mesh: Mesh
  Timer: Stopwatch
}

[<RequireQualifiedAccess>]
module Skybox =

  let private vertexSrc =
    """
#version 330

in vec3 vertexPosition;

uniform mat4 matModel;
uniform mat4 viewProj;

out vec3 vDir;

void main() {
  // Pass the raw vertex position as the sky direction (before model scale/translate).
  vDir = normalize(vertexPosition);
  vec4 worldPos = matModel * vec4(vertexPosition, 1.0);
  gl_Position = viewProj * worldPos;
}
"""

  let private fragmentSrc =
    """
#version 330

in vec3 vDir;

uniform float time;
uniform vec3 horizonColor;
uniform vec3 zenithColor;

out vec4 fragColor;

// 3D hash — deterministic pseudo-random per grid cell.
float hash13(vec3 p) {
  p = fract(p * 0.1031);
  p += dot(p, p.yzx + 33.33);
  return fract((p.x + p.y) * p.z);
}

// Value noise for soft nebula clouds.
float vnoise(vec3 x) {
  vec3 i = floor(x);
  vec3 f = fract(x);
  f = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(mix(hash13(i + vec3(0,0,0)), hash13(i + vec3(1,0,0)), f.x),
        mix(hash13(i + vec3(0,1,0)), hash13(i + vec3(1,1,0)), f.x), f.y),
    mix(mix(hash13(i + vec3(0,0,1)), hash13(i + vec3(1,0,1)), f.x),
        mix(hash13(i + vec3(0,1,1)), hash13(i + vec3(1,1,1)), f.x), f.y),
    f.z);
}

float fbm(vec3 p) {
  float v = 0.0;
  float a = 0.5;
  for (int i = 0; i < 4; i++) {
    v += a * vnoise(p);
    p *= 2.0;
    a *= 0.5;
  }
  return v;
}

// Grid-based star field — each cell may contain one star with a random
// sub-cell position, brightness, and twinkle phase.
float starField(vec3 dir, float density, float brightness) {
  vec3 p = dir * density;
  vec3 cell = floor(p);
  vec3 f = fract(p);

  float h = hash13(cell);
  // Sparse stars: only ~5% of cells light up.
  if (h < 0.95) return 0.0;

  // Random sub-cell offset so stars aren't on a perfect grid.
  vec3 off = vec3(hash13(cell + 1.7), hash13(cell + 3.3), hash13(cell + 7.1)) - 0.5;
  float d = length(f - 0.5 - off * 0.6);

  // Sharp circular star with soft glow.
  float star = smoothstep(0.12, 0.0, d);
  float glow = smoothstep(0.35, 0.0, d) * 0.25;

  // Twinkle.
  float twinkle = 0.6 + 0.4 * sin(time * 2.0 + h * 100.0);

  return (star + glow) * brightness * twinkle;
}

void main() {
  vec3 dir = normalize(vDir);

  // ── Gradient sky ──
  float t = clamp(dir.y * 0.5 + 0.5, 0.0, 1.0);
  vec3 sky = mix(horizonColor, zenithColor, pow(t, 0.8));

  // ── Nebula bands ──
  float nebula = fbm(dir * 3.0 + vec3(time * 0.02, 0.0, 0.0));
  vec3 nebCol1 = vec3(0.15, 0.02, 0.20);
  vec3 nebCol2 = vec3(0.02, 0.08, 0.15);
  float nebMask = smoothstep(0.45, 0.75, nebula);
  sky += mix(nebCol1, nebCol2, nebula) * nebMask * 0.5;

  // ── Star layers (three densities for depth) ──
  float stars = 0.0;
  stars += starField(dir, 40.0,  1.0);  // bright nearby
  stars += starField(dir, 80.0,  0.7);  // medium
  stars += starField(dir, 150.0, 0.4);  // faint distant

  // Star color: warm white with occasional blue tint.
  float starColor = hash13(floor(dir * 80.0) + 11.0);
  vec3 starCol = mix(vec3(1.0, 0.95, 0.8), vec3(0.7, 0.85, 1.0), starColor);
  sky += stars * starCol;

  fragColor = vec4(sky, 1.0);
}
"""

  let create() : SkyboxModel =
    let shader = Raylib.LoadShaderFromMemory(vertexSrc, fragmentSrc)
    let mutable mat = Raylib.LoadMaterialDefault()
    mat.Shader <- shader

    {
      Shader = shader
      Material = mat
      Mesh = Raylib.GenMeshSphere(1.0f, 24, 24)
      Timer = Stopwatch.StartNew()
    }

  let inline private setShaderVec3 (shader: Shader) (loc: int) (v: Vector3) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Vec3
      )

  let inline private setShaderFloat (shader: Shader) (loc: int) (v: float32) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Float
      )

  /// Draws the starry sky dome. MUST be called inside a camera scope so the
  /// SceneContext has valid view/projection matrices. Disables depth test so
  /// the skybox always renders behind scene geometry, and disables backface
  /// culling because we view the sphere from inside.
  let render
    (sky: SkyboxModel)
    (horizon: Mibo.Color)
    (zenith: Mibo.Color)
    (cameraPos: Vector3)
    (buffer: RenderBuffer3D)
    =
    let t = float32 sky.Timer.Elapsed.TotalSeconds

    setShaderFloat sky.Shader (Raylib.GetShaderLocation(sky.Shader, "time")) t

    setShaderVec3
      sky.Shader
      (Raylib.GetShaderLocation(sky.Shader, "horizonColor"))
      (Vector3(
        float32 horizon.R / 255.0f,
        float32 horizon.G / 255.0f,
        float32 horizon.B / 255.0f
      ))

    setShaderVec3
      sky.Shader
      (Raylib.GetShaderLocation(sky.Shader, "zenithColor"))
      (Vector3(
        float32 zenith.R / 255.0f,
        float32 zenith.G / 255.0f,
        float32 zenith.B / 255.0f
      ))

    let scale = 500.0f

    let transform =
      Matrix4x4.CreateScale(scale)
      |> fun s -> Matrix4x4.Multiply(s, Matrix4x4.CreateTranslation(cameraPos))

    buffer
    |> Draw3D.drawImmediate(fun scene ->
      Raylib.BeginShaderMode sky.Shader
      Rlgl.DisableBackfaceCulling()
      Rlgl.DisableDepthTest()

      let vp = Matrix4x4.Multiply(scene.View, scene.Projection)

      Raylib.SetShaderValueMatrix(
        sky.Shader,
        Raylib.GetShaderLocation(sky.Shader, "viewProj"),
        vp
      )

      Raylib.SetShaderValueMatrix(
        sky.Shader,
        Raylib.GetShaderLocation(sky.Shader, "matModel"),
        transform
      )

      Raylib.DrawMesh(sky.Mesh, sky.Material, transform)

      Rlgl.EnableDepthTest()
      Rlgl.EnableBackfaceCulling()
      Raylib.EndShaderMode())
    |> Draw3D.drop
