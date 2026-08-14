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
// EnemiesView — UFO hulls, boss aura rings and health bars from the
// frame's Alive/Defs snapshots (read as plain dictionary values —
// no graph access at draw). One instanced draw per hull model
// (InstanceScratch groups by model path, so the boss — the grunt
// hull at 1.6 × EnemyLayout.enemyScale — shares the grunt group
// with a scaled instance matrix). Models are bottom-anchored (MapView
// module header); the hull rests on the shared EnemyLayout.hoverY
// (0.2 for walkers, 0.8 for fliers), with a deterministic hover bob
// and a slow idle spin.
//
// Health bars are camera-facing billboard quads (shared white
// texture, DepthRead — no depth write): the fill quad blends over
// the background quad, drawn in one batch per frame.
// ─────────────────────────────────────────────────────────────

module EnemiesView =

  /// Hulls (untinted) and the boss aura rings (tinted, translucent —
  /// selection-b) go through the shared InstanceScratch: reset →
  /// fill → draw per frame, zero allocation once warm.

  // Health-bar billboard scratch (XNA arrays — the billboardBatch
  // payload). Grow-only, reused across frames.
  let mutable private barTextures = Array.zeroCreate<Texture2D> 1
  let mutable private barPositions = Array.empty<Vector3>
  let mutable private barSizes = Array.empty<Vector2>
  let mutable private barColors = Array.empty<Color>

  let mutable private barCount = 0

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

  /// selection-b's outer vertex radius (measured via vertex probe:
  /// the octagon's corners sit at (±0.5, ±0.4) → √(0.5² + 0.4²) ≈
  /// 0.6403). Aura/ring scales divide the sim radius by this so the
  /// octagon's corners land exactly on the sim's circle.
  let private selectionBVertexRadius = 0.6403f

  /// The boss aura ring: selection-b flattened to a thin band (the
  /// 0.2-tall mesh at 0.5 Y-scale reads as a ground ring), scaled so
  /// its outer vertices land exactly on the sim radius
  /// (BossAura.Radius — the aura must not read bigger than the
  /// suppression radius), translucent red per-instance tint (alpha
  /// routes the draw through the pipeline's sorted translucent pass).
  let private auraTransform(v: EnemyView) : Matrix =
    let s = BossAura.Radius / selectionBVertexRadius

    Matrix.CreateScale(s, 0.5f, s)
    * Matrix.CreateTranslation(v.Pos.X, 0f, v.Pos.Y)

  let private auraColor = Color(255, 60, 60, 70)

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

  /// Hulls, aura rings and health bars from the frame's Alive/Defs
  /// snapshots.
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
    let time = Time.now()

    InstanceScratch.reset()
    barCount <- 0

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
          InstanceScratch.addTinted
            Models.selectionB.Path
            (auraTransform v)
            auraColor

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

    // Health bars: one billboardBatch (backgrounds then fills — the
    // fill quads blend over the backgrounds; DepthRead keeps them
    // hidden behind hulls).
    if barCount > 0 then
      let gd = MonoGameGameContext.getGraphicsDevice ctx
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
