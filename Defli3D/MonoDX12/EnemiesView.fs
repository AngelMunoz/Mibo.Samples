namespace Defli3D.MonoGame

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// EnemiesView — UFO hulls, boss body aura and health bars from the
// frame's Alive/Defs snapshots (read as plain dictionary values —
// no graph access at draw). One instanced draw per hull model
// (InstanceScratch groups by model path, so the boss — the grunt
// hull at 1.6 × EnemyLayout.enemyScale — shares the grunt group
// with a scaled instance matrix). Models are bottom-anchored (MapView
// module header); the hull rests on the shared EnemyLayout.hoverY
// (0.2 for walkers, 0.8 for fliers), with a deterministic hover bob
// and a slow idle spin.
//
// The boss aura is a fresnel SHELL — a unit sphere (Primitive3D.Sphere)
// scaled to BossAura.VisualRadius and centered on the hull, drawn with
// the Aura effect through DrawImmediate so the view owns the blend
// (AlphaBlend) and depth (DepthRead — test on, write off): the
// beginEffect scope runs its draws inline with the pass's OPAQUE state,
// which would make the shell solid. The hull is drawn first (depth
// written) so the aura's back hemisphere is occluded; the fresnel makes
// the rim read as a glow around the boss.
//
// Health bars are camera-facing billboard quads (shared white
// texture, DepthRead — no depth write): the fill quad blends over
// the background quad, drawn in one batch per frame.
// ─────────────────────────────────────────────────────────────

module EnemiesView =

  /// Hulls (untinted) go through the shared InstanceScratch: reset → fill →
  /// draw per frame, zero allocation once warm.

  // Health-bar billboard scratch (XNA arrays — the billboardBatch
  // payload). Grow-only, reused across frames.
  let mutable private barTextures = Array.zeroCreate<Texture2D> 1
  let mutable private barPositions = Array.empty<Vector3>
  let mutable private barSizes = Array.empty<Vector2>
  let mutable private barColors = Array.empty<Color>

  let mutable private barCount = 0

  // ── Boss body aura (fresnel shell via DrawImmediate) ──
  // Aura.fx is a fresnel rim shader; the unit sphere is scaled around the
  // boss body. Loaded lazily on the first frame a boss is alive.
  let mutable private auraEffect: Effect voption = ValueNone

  let mutable private auraPrimitives: Primitive3D.PrimitiveSet voption =
    ValueNone

  /// Per-frame boss body centers (X, hull-center Y, Z), filled during the
  /// hull pass and consumed by the aura DrawImmediate after the hulls draw.
  let private bossCenters = ResizeArray<Vector3>()

  /// Aura tuning (matches Aura.fx uniform names).
  let private auraTint = Vector3(1.0f, 0.25f, 0.25f)
  let private auraPower = 2.5f
  let private auraIntensity = 0.6f

  /// Loads Aura.fx + the unit primitives once, and sets the constant aura
  /// tuning uniforms. Idempotent.
  let private ensureAura (assets: IAssets) (gd: GraphicsDevice) =
    match auraEffect with
    | ValueSome _ -> ()
    | ValueNone ->
      let e = assets.Effect "Aura"

      e.Parameters.["auraColor"].SetValue(auraTint)
      e.Parameters.["auraPower"].SetValue(auraPower)
      e.Parameters.["auraIntensity"].SetValue(auraIntensity)
      auraEffect <- ValueSome e
      auraPrimitives <- ValueSome(Primitive3D.create gd)

  /// Deterministic hover bob: fixed-frequency sine with a per-enemy
  /// phase (id-derived — stable across despawns, unlike an
  /// enumeration index), riding on the shared EnemyLayout.hoverY
  /// anchor (0.2 walkers / 0.8 fliers).
  let inline private hoverY
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (time: float32)
    : float32 =
    let phase = float32(int eid % 9) * 0.7f
    EnemyLayout.hoverY def + 0.05f * MathF.Sin(time * 2f + phase)

  /// Slow idle spin (radians), phase-per-enemy.
  let inline private spinY (eid: int<EnemyId>) (time: float32) : float32 =
    let phase = float32(int eid % 7) * 0.9f
    time * 0.5f + phase

  /// The hull transform: scale (boss 1.6 × EnemyLayout.enemyScale) ·
  /// idle spin · translation at (x, hover, z).
  let private hullTransform
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (v: EnemyView)
    (time: float32)
    : Matrix =
    Matrix.CreateScale(def.Scale * EnemyLayout.enemyScale)
    * Matrix.CreateRotationY(spinY eid time)
    * Matrix.CreateTranslation(v.Pos.X, hoverY def eid time, v.Pos.Y)

  /// The boss aura sphere center Y: the bobbed hull center (the hull
  /// spans [hoverY, hoverY + scaled hull height]; its vertical middle).
  let inline private auraCenterY
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (time: float32)
    : float32 =
    hoverY def eid time
    + def.HullModel.SizeY * def.Scale * EnemyLayout.enemyScale * 0.5f

  /// The health-bar center height above a hull (world units).
  let inline private barY
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (v: EnemyView)
    (time: float32)
    : float32 =
    hoverY def eid time
    + def.HullModel.SizeY * def.Scale * EnemyLayout.enemyScale
    + 0.15f * def.Scale * EnemyLayout.enemyScale

  /// Hulls, boss body auras and health bars from the frame's Alive/Defs
  /// snapshots.
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
    let time = Time.now()

    InstanceScratch.reset()
    bossCenters.Clear()
    barCount <- 0

    let assets = GameContext.getService<IAssets> ctx
    let gd = MonoGameGameContext.getGraphicsDevice ctx

    // Count damaged enemies first (the bar scratch needs a size).
    for KeyValueV(_, v) in frame.Alive do
      if v.Hp < v.MaxHp then
        barCount <- barCount + 1

    if barCount > 0 then
      let needed = barCount * 2

      if barPositions.Length < needed then
        barPositions <- Array.zeroCreate needed
        barSizes <- Array.zeroCreate needed
        barColors <- Array.zeroCreate needed

    // Fill the hull/aura batches + the bar quads.
    let mutable barSlot = 0

    for KeyValueV(eid, v) in frame.Alive do
      match frame.Defs |> ReadOnlyDict.tryGetValue eid with
      | ValueNone -> ()
      | ValueSome def ->
        InstanceScratch.add def.HullModel.Path (hullTransform def eid v time)

        if def.Archetype = EnemyArchetype.Boss then
          // Record the body center; the fresnel shell draws after the
          // hulls (DrawImmediate) so the hull depth occludes its back.
          bossCenters.Add(Vector3(v.Pos.X, auraCenterY def eid time, v.Pos.Y))

        if v.Hp < v.MaxHp then
          let frac = float32 v.Hp / float32 v.MaxHp
          let s = def.Scale * EnemyLayout.enemyScale
          let y = barY def eid v time
          let w = 0.75f * s
          let h = 0.09f * s

          barPositions[barSlot] <- Vector3(v.Pos.X, y, v.Pos.Y)
          barSizes[barSlot] <- Vector2(w, h)
          barColors[barSlot] <- Color(0, 0, 0, 190)
          barSlot <- barSlot + 1

          // The fill shrinks symmetrically about the bar center (the
          // billboard quad is centered on its position).
          barPositions[barSlot] <- Vector3(v.Pos.X, y, v.Pos.Y)
          barSizes[barSlot] <- Vector2(w * frac, h)
          barColors[barSlot] <- Color(215, 45, 45, 230)
          barSlot <- barSlot + 1

    InstanceScratch.draw buffer

    // Boss body auras: one DrawImmediate that draws every boss's fresnel
    // shell. The hulls are already drawn (depth written), so each shell's
    // back hemisphere is depth-occluded; AlphaBlend + DepthRead keep the
    // rim glow from occluding the scene.
    if bossCenters.Count > 0 then
      ensureAura assets gd

      match auraEffect, auraPrimitives with
      | ValueSome effect, ValueSome primitives ->
        buffer
          .drawImmediate(fun scene ->
            let gd = scene.Device
            let sphere = primitives.Sphere
            let viewProj = scene.View * scene.Projection
            let cp = scene.Camera.Position
            let camPos = Vector3(float32 cp.X, float32 cp.Y, float32 cp.Z)

            effect.Parameters.["viewProj"].SetValue(viewProj)
            effect.Parameters.["cameraPos"].SetValue(camPos)

            let prevBlend = gd.BlendState
            let prevDepth = gd.DepthStencilState

            gd.BlendState <- BlendState.AlphaBlend
            gd.DepthStencilState <- DepthStencilState.DepthRead

            for i = 0 to bossCenters.Count - 1 do
              let center = bossCenters[i]

              let transform =
                Matrix.CreateScale(BossAura.VisualRadius)
                * Matrix.CreateTranslation(center)

              let mutable t = transform
              let mutable inv = Matrix.Identity
              Matrix.Invert(&t, &inv) |> ignore
              let normalMatrix = Matrix.Transpose inv

              effect.Parameters.["matModel"].SetValue(transform)
              effect.Parameters.["normalMatrix"].SetValue(normalMatrix)
              sphere.Draw(gd, effect)

            gd.DepthStencilState <- prevDepth
            gd.BlendState <- prevBlend)
          .drop()

    // Health bars: one billboardBatch (backgrounds then fills — the
    // fill quads blend over the backgrounds; DepthRead keeps them
    // hidden behind hulls).
    if barCount > 0 then
      barTextures[0] <- WhiteTex.get gd

      buffer
        .billboardBatch(
          barTextures,
          barPositions,
          barSizes,
          barColors,
          barCount * 2
        )
        .drop()
