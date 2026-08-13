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
// EnemiesView — UFO hulls, weapons, boss aura rings and health bars
// from the frame's Alive/Defs snapshots (read as plain dictionary
// values — no graph access at draw). One instanced draw per hull /
// weapon model (InstanceScratch groups by model path, so the boss —
// the grunt hull at 1.6 × enemyScale — shares the grunt group with a
// scaled instance matrix). Models are bottom-anchored (MapView module
// header); the hull base height is 0.2 (0.8 for fliers), with a
// deterministic hover bob and a slow idle spin.
//
// Weapons are top-mounted on the hull and aimed at the heading (the
// direction to the next waypoint — the 2D port's heading convention;
// yaw = atan2(dx, dz) assumes the weapon's barrel along +Z).
// Health bars are camera-facing billboard quads (shared white
// texture, DepthRead — no depth write): the fill quad blends over
// the background quad, drawn in one batch per frame.
// ─────────────────────────────────────────────────────────────

module EnemiesView =

  /// Visual scale of enemy hulls + weapons (1 = model size; UFO
  /// hulls are 1.0 wide). 0.7 reads better next to the scaled
  /// towers — tune to taste. Bosses still ride their def.Scale
  /// (1.6) ON TOP of this constant; the aura ring keeps the SIM
  /// radius (BossAura.Radius — do NOT scale).
  let enemyScale = 0.7f

  /// Hulls + weapons (untinted) and the boss aura rings (tinted,
  /// translucent — selection-b) go through the shared InstanceScratch:
  /// reset → fill → draw per frame, zero allocation once warm.

  // Health-bar billboard scratch (XNA arrays — the billboardBatch
  // payload). Grow-only, reused across frames.
  let mutable private barTextures = Array.zeroCreate<Texture2D> 1
  let mutable private barPositions = Array.empty<Vector3>
  let mutable private barSizes = Array.empty<Vector2>
  let mutable private barColors = Array.empty<Color>

  let mutable private barCount = 0

  /// Hull base height (world units): fliers ride higher above the
  /// ground plane.
  let inline private baseHeight(def: EnemyDef) : float32 =
    if def.Archetype = EnemyArchetype.Flier then 0.8f else 0.2f

  /// Deterministic hover bob: fixed-frequency sine with a per-enemy
  /// phase (id-derived — stable across despawns, unlike an
  /// enumeration index).
  let inline private hoverY
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (time: float32)
    : float32 =
    let phase = float32(int eid % 9) * 0.7f
    baseHeight def + 0.05f * MathF.Sin(time * 2f + phase)

  /// Slow idle spin (radians), phase-per-enemy.
  let inline private spinY (eid: int<EnemyId>) (time: float32) : float32 =
    let phase = float32(int eid % 7) * 0.9f
    time * 0.5f + phase

  /// Yaw that aims the weapon along the enemy's heading — the
  /// direction to the next waypoint (0 on the last segment).
  let inline private headingYaw
    (path: System.Numerics.Vector2[])
    (v: EnemyView)
    : float32 =
    if v.PathIndex >= path.Length - 1 then
      0f
    else
      let d = path[v.PathIndex + 1] - v.Pos
      MathF.Atan2(d.X, d.Y)

  /// The hull transform: scale (boss 1.6 × enemyScale) · idle spin ·
  /// translation at (x, hover, z).
  let private hullTransform
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (v: EnemyView)
    (time: float32)
    : Matrix =
    Matrix.CreateScale(def.Scale * enemyScale)
    * Matrix.CreateRotationY(spinY eid time)
    * Matrix.CreateTranslation(v.Pos.X, hoverY def eid time, v.Pos.Y)

  /// The weapon transform: same position/scale as the hull, aimed at
  /// the heading (top-mounted — the weapon base sits on the hull top).
  let private weaponTransform
    (path: System.Numerics.Vector2[])
    (def: EnemyDef)
    (eid: int<EnemyId>)
    (v: EnemyView)
    (time: float32)
    : Matrix =
    let y = hoverY def eid time + def.HullModel.SizeY * def.Scale * enemyScale

    Matrix.CreateScale(def.Scale * enemyScale)
    * Matrix.CreateRotationY(headingYaw path v)
    * Matrix.CreateTranslation(v.Pos.X, y, v.Pos.Y)

  /// The boss aura ring: selection-b scaled to BossAura.Radius × 2,
  /// translucent red per-instance tint (alpha routes the draw through
  /// the pipeline's sorted translucent pass).
  let private auraTransform(v: EnemyView) : Matrix =
    Matrix.CreateScale(BossAura.Radius * 2f)
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
    + def.HullModel.SizeY * def.Scale * enemyScale
    + 0.15f * def.Scale * enemyScale

  /// Hulls, weapons, aura rings and health bars from the frame's
  /// Alive/Defs snapshots.
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
    let time = Time.now()
    let path = frame.Map.Path

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

    // Fill the hull/weapon/aura batches + the bar quads.
    let mutable barSlot = 0

    for KeyValueV(eid, v) in frame.Alive do
      match frame.Defs |> ReadOnlyDict.tryGetValue eid with
      | ValueNone -> ()
      | ValueSome def ->
        InstanceScratch.add def.HullModel.Path (hullTransform def eid v time)

        match def.WeaponModel with
        | ValueSome weapon ->
          InstanceScratch.add weapon.Path (weaponTransform path def eid v time)
        | ValueNone -> ()

        if def.Archetype = EnemyArchetype.Boss then
          InstanceScratch.addTinted
            Models.selectionB.Path
            (auraTransform v)
            auraColor

        if v.Hp < v.MaxHp then
          let frac = float32 v.Hp / float32 v.MaxHp
          let s = def.Scale * enemyScale
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
