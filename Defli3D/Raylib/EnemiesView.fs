namespace Defli3D.Raylib

open System
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open FSharp.NativeInterop
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems

#nowarn "9"

// ─────────────────────────────────────────────────────────────
// EnemiesView — enemy hulls from the frame's Alive/Defs snapshots
// (transient views read as plain dictionaries — no graph access at
// draw; the sim clock rides the frame as frame.Time). One .instanced
// draw per hull model (the view's own InstanceGroups); health bars
// are a single billboardBatch above the enemies; bosses get a
// fresnel body aura (a unit sphere scaled around the hull, drawn
// with the aura shader through DrawImmediate so the view owns the
// blend + depth-write — the beginEffect scope runs draws inline with
// the pass's default state).
//
// Motion is VIEW-edge presentation on top of the sim's XZ positions:
//   * hover bob — deterministic: sin(time · 2.2 + id-based phase),
//     ground enemies hover around y = 0.2 (tile top), fliers ~0.8.
//   * slow spin — the hull rotates lazily around +Y (time + phase).
//   * boss — def.Scale (1.6) scales the hull ON TOP of the shared
//     EnemyLayout.enemyScale.
// ─────────────────────────────────────────────────────────────

module EnemiesView =

  // ── Boss body aura: fresnel shell (GLSL) via DrawImmediate ──
  // A unit sphere (Primitive3D.sphere) scaled to BossAura.VisualRadius and
  // centered on the hull. The fresnel makes the rim read as a glow; the
  // hull is drawn first (depth written) so the shell's back hemisphere is
  // depth-occluded. Same uniform names as the MonoGame Aura.fx contract.
  // DrawMesh binds the MATERIAL's shader (rmodels.c — BeginShaderMode is
  // overridden for the draw), so the material must carry the aura shader.

  let auraVs =
    "
#version 330
in vec3 vertexPosition;
in vec3 vertexNormal;

uniform mat4 matModel;
uniform mat4 viewProj;
uniform mat4 normalMatrix;

out vec3 vWorldPos;
out vec3 vWorldNormal;

void main() {
  vec4 world = matModel * vec4(vertexPosition, 1.0);
  gl_Position = viewProj * world;
  vWorldPos = world.xyz;
  vWorldNormal = mat3(normalMatrix) * vertexNormal;
}
"

  let auraFs =
    "
#version 330
in vec3 vWorldPos;
in vec3 vWorldNormal;

uniform vec3 cameraPos;
uniform vec3 auraColor;
uniform float auraPower;
uniform float auraIntensity;

out vec4 finalColor;

void main() {
  vec3 N = normalize(vWorldNormal);
  vec3 V = normalize(cameraPos - vWorldPos);
  // Fresnel: ~0 facing the camera, ~1 at the silhouette.
  float fresnel = pow(clamp(1.0 - max(dot(N, V), 0.0), 0.0, 1.0), auraPower);
  // A small floor tints the whole volume faintly; the rim is brightest.
  float a = clamp(auraIntensity * (0.20 + 0.80 * fresnel), 0.0, 1.0);
  finalColor = vec4(auraColor, a);
}
"

  /// Shader uniform locations + the material DrawMesh needs (its shader
  /// must be the aura shader — DrawMesh binds material.shader).
  [<Struct>]
  type AuraShader = {
    Shader: Shader
    ViewProj: int
    MatModel: int
    NormalMatrix: int
    CameraPos: int
    AuraColor: int
    AuraPower: int
    AuraIntensity: int
    Material: Material
  }

  /// Aura tuning (matches auraFs uniform names).
  let auraTint = Vector3(1.0f, 0.25f, 0.25f)

  let auraPower = 2.5f

  let auraIntensity = 0.6f

  let inline setShaderFloat (shader: Shader) (loc: int) (v: float32) =
    let mutable value = v
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Float
    )

  let inline setShaderVec3 (shader: Shader) (loc: int) (v: Vector3) =
    let mutable value = v
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec3
    )

  /// Deterministic per-enemy phase (id-based) — no RNG at draw time.
  let inline phaseOf(eid: int<EnemyId>) : float32 =
    float32(int(eid % 7<EnemyId>)) * 0.9f

/// The enemies presenter: hulls, boss body auras and health bars.
/// Owns its scratch (instance groups, bar quads, the lazy aura
/// shader + material, the 1×1 white texture) — constructed once in
/// Program.fs, no module-level mutable state.
[<Sealed>]
type EnemiesView() =

  let groups = InstanceGroups()

  // ── Boss body aura (fresnel shell via DrawImmediate) ──
  // Loaded lazily on the first frame a boss is alive. raylib calls
  // need an open window. The material's shader is the aura shader
  // (DrawMesh binds material.shader); the tuning uniforms are set
  // per-frame while the shader is current (uniform uploads hit the
  // CURRENT program, so they cannot be set at load time).
  let mutable auraShader: EnemiesView.AuraShader voption = ValueNone

  /// Loads the aura shader + the material DrawMesh needs, and caches
  /// the uniform locations. Idempotent.
  let ensureAura() : EnemiesView.AuraShader =
    match auraShader with
    | ValueSome s -> s
    | ValueNone ->
      let shader =
        Raylib.LoadShaderFromMemory(EnemiesView.auraVs, EnemiesView.auraFs)

      let mutable material = Raylib.LoadMaterialDefault()
      material.Shader <- shader

      let s: EnemiesView.AuraShader = {
        Shader = shader
        ViewProj = Raylib.GetShaderLocation(shader, "viewProj")
        MatModel = Raylib.GetShaderLocation(shader, "matModel")
        NormalMatrix = Raylib.GetShaderLocation(shader, "normalMatrix")
        CameraPos = Raylib.GetShaderLocation(shader, "cameraPos")
        AuraColor = Raylib.GetShaderLocation(shader, "auraColor")
        AuraPower = Raylib.GetShaderLocation(shader, "auraPower")
        AuraIntensity = Raylib.GetShaderLocation(shader, "auraIntensity")
        Material = material
      }

      auraShader <- ValueSome s
      s

  /// Per-frame boss body centers (X, hull-center Y, Z), filled during
  /// the hull pass and consumed by the aura DrawImmediate after the
  /// hulls draw.
  let bossCenters = ResizeArray<Vector3>()

  // Grow-only scratch for the health-bar billboard batch (two quads
  // per enemy: black backing + red fill). Reused every frame; grows
  // only when a frame needs more room.
  let mutable barTextures = Array.empty<Texture2D>

  let mutable barPositions = Array.empty<Vector3>

  let mutable barSizes = Array.empty<Vector2>

  let mutable barColors = Array.empty<Raylib_cs.Color>

  /// The 1×1 white texture the billboard batch tints — generated
  /// lazily on the first draw (Raylib calls need an open window).
  let mutable whiteTex: Texture2D voption = ValueNone

  let whiteTexture() : Texture2D =
    match whiteTex with
    | ValueSome t -> t
    | ValueNone ->
      let img =
        Raylib.GenImageColor(1, 1, Raylib_cs.Color(255uy, 255uy, 255uy, 255uy))

      let tex = Raylib.LoadTextureFromImage(img)
      Raylib.UnloadImage(img)
      whiteTex <- ValueSome tex
      tex

  /// Hulls, boss body auras and health bars from the frame's
  /// Alive/Defs snapshots.
  member _.View(ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer3D) =
    let time = float32 frame.Time.TotalTime.TotalSeconds
    groups.Clear()
    bossCenters.Clear()

    let tex = whiteTexture()

    // Count damaged enemies first (the bar scratch needs a size).
    let mutable barCount = 0

    for KeyValueV(_, v) in frame.Alive do
      if v.Hp < v.MaxHp then
        barCount <- barCount + 1

    if barCount > 0 then
      let needed = barCount * 2

      if barPositions.Length < needed then
        barTextures <- Array.zeroCreate needed
        barPositions <- Array.zeroCreate needed
        barSizes <- Array.zeroCreate needed
        barColors <- Array.zeroCreate needed

    let mutable barSlot = 0

    for KeyValueV(eid, v) in frame.Alive do
      frame.Defs
      |> ReadOnlyDict.tryGetValue eid
      |> ValueOption.iter(fun def ->
        let isBoss = def.Archetype = EnemyArchetype.Boss
        let phase = EnemiesView.phaseOf eid

        // Hover bob + slow spin around the sim's XZ position. The
        // resting height is the shared EnemyLayout.hoverY (tile top
        // for walkers, flight altitude for fliers).
        let baseY = EnemyLayout.hoverY def
        let y = baseY + 0.06f * MathF.Sin(time * 2.2f + phase)
        let spin = time * 0.8f + phase
        let scale = def.Scale * EnemyLayout.enemyScale
        let pos = Vector3(v.Pos.X, y, v.Pos.Y)

        // Raymath ops produce raylib's native (GLSL column-major) layout, so
        // the instanced attribute reads correctly: spin about the hull's own
        // axis, then place at pos.
        let scaleM = Raymath.MatrixScale(scale, scale, scale)
        let spinM = Raymath.MatrixRotateY(spin)
        let transM = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

        groups.Add(
          def.HullModel.Name,
          Raymath.MatrixMultiply(Raymath.MatrixMultiply(scaleM, spinM), transM)
        )

        // Boss body aura: record the hull's vertical center (bobbed). The
        // fresnel shell draws after the hulls (DrawImmediate) so the hull
        // depth occludes its back hemisphere.
        if isBoss then
          let centerY = y + def.HullModel.SizeY * scale * 0.5f
          bossCenters.Add(Vector3(v.Pos.X, centerY, v.Pos.Y))

        // Health bar (only when damaged): black backing + red fill,
        // recorded into the shared billboard batch. Sizes follow the
        // scaled hull.
        if v.Hp < v.MaxHp then
          let frac = float32 v.Hp / float32 v.MaxHp
          let yBar = y + 0.35f + 0.55f * scale
          let barW = 0.9f * scale
          let barH = 0.09f * scale

          barTextures[barSlot] <- tex
          barPositions[barSlot] <- Vector3(v.Pos.X, yBar, v.Pos.Y)
          barSizes[barSlot] <- Vector2(barW, barH)
          barColors[barSlot] <- Raylib_cs.Color(0uy, 0uy, 0uy, 200uy)
          barSlot <- barSlot + 1

          barTextures[barSlot] <- tex
          barPositions[barSlot] <- Vector3(v.Pos.X, yBar, v.Pos.Y)
          barSizes[barSlot] <- Vector2(barW * frac, barH)
          barColors[barSlot] <- Raylib_cs.Color(230uy, 40uy, 40uy, 230uy)
          barSlot <- barSlot + 1)

    groups.Draw buffer

    // Boss body auras: one DrawImmediate over every boss's fresnel shell.
    // DrawImmediate runs outside BeginMode3D (which disabled depth test),
    // so re-enable depth TEST (the hull occludes each shell's back), turn
    // depth WRITE off (the rim must not occlude the scene), and use the
    // default alpha blend (straight — the shell's tint scales with alpha).
    if bossCenters.Count > 0 then
      let aura = ensureAura()
      let shader = aura.Shader

      buffer
        .drawImmediate(fun scene ->
          let vp = Raymath.MatrixMultiply(scene.View, scene.Projection)

          Raylib.SetShaderValueMatrix(shader, aura.ViewProj, vp)

          EnemiesView.setShaderVec3 shader aura.CameraPos scene.Camera.Position

          Rlgl.EnableDepthTest()
          Rlgl.DisableDepthMask()
          Raylib.BeginBlendMode BlendMode.Alpha
          Raylib.BeginShaderMode shader

          // Tuning constants — uniform uploads hit the CURRENT program,
          // so these must be set while the aura shader is bound.
          EnemiesView.setShaderVec3 shader aura.AuraColor EnemiesView.auraTint

          EnemiesView.setShaderFloat
            shader
            aura.AuraPower
            EnemiesView.auraPower

          EnemiesView.setShaderFloat
            shader
            aura.AuraIntensity
            EnemiesView.auraIntensity

          let r = BossAura.VisualRadius

          for i = 0 to bossCenters.Count - 1 do
            let center = bossCenters[i]

            let transform =
              Raymath.MatrixMultiply(
                Raymath.MatrixScale(r, r, r),
                Raymath.MatrixTranslate(center.X, center.Y, center.Z)
              )

            // Uniform scale: matModel works as the normal matrix (the
            // fragment normalizes), avoiding a transpose(inverse).
            // DrawMesh ends by unbinding the program (rlDisableShader),
            // so re-bind before each draw's uniform uploads.
            Rlgl.EnableShader shader.Id
            Raylib.SetShaderValueMatrix(shader, aura.MatModel, transform)
            Raylib.SetShaderValueMatrix(shader, aura.NormalMatrix, transform)
            Raylib.DrawMesh(Primitive3D.sphere, aura.Material, transform)

          Raylib.EndShaderMode()
          Raylib.EndBlendMode()
          Rlgl.EnableDepthMask())
        .drop()

    // All health bars in one batch (buffer order — drawn after the
    // bodies, so they read on top).
    if barCount > 0 then
      buffer
        .billboardBatch(
          barTextures,
          barPositions,
          barSizes,
          barColors,
          barCount * 2
        )
        .drop()
