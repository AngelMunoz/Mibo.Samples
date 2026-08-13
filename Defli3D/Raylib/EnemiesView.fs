namespace Defli3D.Raylib

open System
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// EnemiesView — enemy hulls + weapons from the frame's Alive/Defs
// snapshots (transient views read as plain dictionaries — no graph
// access at draw). One .instanced draw per hull model and per weapon
// model (shared InstanceScratch); health bars are a single
// billboardBatch above the enemies; bosses get a semi-transparent
// aura ring (Models.selectionB scaled to BossAura.Radius × 2).
//
// Motion is VIEW-edge presentation on top of the sim's XZ positions:
//   * hover bob — deterministic: sin(time · 2.2 + id-based phase),
//     ground enemies hover around y = 0.2 (tile top), fliers ~0.8.
//   * slow spin — the hull rotates lazily around +Y (time + phase);
//     the mounted weapon instead aims at the travel heading (next
//     waypoint — Defli's 2D heading), so saucers read as alive while
//     still steering down the road.
//   * boss — def.Scale (1.6) scales hull + weapon ON TOP of the
//     enemyScale constant (the aura ring keeps the sim radius).
// Time comes from Raylib.GetTime() (the view has no GameTime — the
// renderer draws after the sim, so the same value is stable per frame).
// ─────────────────────────────────────────────────────────────

module EnemiesView =

  /// Visual scale of enemy hulls + weapons (1 = model size; UFO
  /// hulls are 1.0 wide). 0.7 reads better next to the scaled
  /// towers — tune to taste. Bosses still ride their def.Scale
  /// (1.6) ON TOP of this constant; the aura ring keeps the SIM
  /// radius (BossAura.Radius — do NOT scale).
  let enemyScale = 0.7f

  /// Grow-only scratch for the health-bar billboard batch (two quads
  /// per enemy: black backing + red fill). Preallocated, reused every
  /// frame — zero per-frame allocation.
  [<Literal>]
  let private barCapacity = 256

  let private barTextures = Array.zeroCreate<Texture2D> barCapacity
  let private barPositions = Array.zeroCreate<Vector3> barCapacity
  let private barSizes = Array.zeroCreate<Vector2> barCapacity
  let private barColors = Array.zeroCreate<Raylib_cs.Color> barCapacity

  /// The 1×1 white texture the billboard batch tints — generated
  /// lazily on the first draw (Raylib calls need an open window).
  let mutable private whiteTex: Texture2D voption = ValueNone

  let private whiteTexture() : Texture2D =
    match whiteTex with
    | ValueSome t -> t
    | ValueNone ->
      let img =
        Raylib.GenImageColor(1, 1, Raylib_cs.Color(255uy, 255uy, 255uy, 255uy))

      let tex = Raylib.LoadTextureFromImage(img)
      Raylib.UnloadImage(img)
      whiteTex <- ValueSome tex
      tex

  /// Deterministic per-enemy phase (id-based) — no RNG at draw time.
  let inline private phaseOf(eid: int<EnemyId>) : float32 =
    float32(int(eid % 7<EnemyId>)) * 0.9f

  /// The travel heading (radians, yaw around +Y): fliers fly the
  /// straight spawn → base line; the rest aim at the next waypoint
  /// (0 rad = facing +Z; the weapon models' forward is +Z).
  let inline private headingYaw
    (v: EnemyView)
    (def: EnemyDef)
    (path: Vector2[])
    =
    if def.Archetype = EnemyArchetype.Flier then
      let d = path[path.Length - 1] - path[0]
      MathF.Atan2(d.X, d.Y)
    elif v.PathIndex >= path.Length - 1 then
      0f
    else
      let d = path[v.PathIndex + 1] - v.Pos
      MathF.Atan2(d.X, d.Y)

  /// Hulls + weapons go through the shared InstanceScratch (grouped
  /// by model name): reset → fill → draw per frame, zero allocation
  /// once warm.
  let view
    (ctx: GameContext)
    (alive: IReadOnlyDictionary<int<EnemyId>, EnemyView>)
    (defs: IReadOnlyDictionary<int<EnemyId>, EnemyDef>)
    (path: Vector2[])
    (buffer: RenderBuffer3D)
    =
    let time = float32(Raylib.GetTime())
    InstanceScratch.reset()

    let tex = whiteTexture()
    let mutable barCount = 0

    for KeyValueV(eid, v) in alive do
      match defs |> ReadOnlyDict.tryGetValue eid with
      | ValueNone -> ()
      | ValueSome def ->
        let isBoss = def.Archetype = EnemyArchetype.Boss
        let phase = phaseOf eid

        // Hover bob + slow spin around the sim's XZ position.
        let baseY = if def.Archetype = EnemyArchetype.Flier then 0.8f else 0.2f

        let y = baseY + 0.06f * MathF.Sin(time * 2.2f + phase)
        let spin = time * 0.8f + phase
        let scale = def.Scale * enemyScale
        let pos = Vector3(v.Pos.X, y, v.Pos.Y)

        // Raymath ops produce raylib's native (GLSL column-major) layout, so
        // the instanced attribute reads correctly: spin about the hull's own
        // axis, then place at pos.
        let scaleM = Raymath.MatrixScale(scale, scale, scale)
        let spinM = Raymath.MatrixRotateY(spin)
        let transM = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

        InstanceScratch.add
          def.HullModel.Name
          (Raymath.MatrixMultiply(Raymath.MatrixMultiply(scaleM, spinM), transM))

        // Weapon mount — aimed at the travel heading (the weapon
        // models' forward is +Z), riding the hull's hover, offset
        // scaled with the hull.
        match def.WeaponModel with
        | ValueNone -> ()
        | ValueSome wm ->
          let yaw = headingYaw v def path
          let weaponPos = Vector3(v.Pos.X, y + 0.08f * scale, v.Pos.Y)

          InstanceScratch.add
            wm.Name
            (Raymath.MatrixMultiply(
              Raymath.MatrixMultiply(scaleM, Raymath.MatrixRotateY(yaw)),
              Raymath.MatrixTranslate(weaponPos.X, weaponPos.Y, weaponPos.Z)
            ))

        // Boss aura ring — the suppression radius on the ground,
        // semi-transparent (Material3D.Opacity — one mesh draw per
        // boss, they are rare).
        if isBoss then
          let auraInfo = Models.selectionB
          let auraMeshes = ModelMeshes.resolve auraInfo

          let auraTransform =
            Raymath.MatrixMultiply(
              Raymath.MatrixScale(
                BossAura.Radius * 2f,
                1f,
                BossAura.Radius * 2f
              ),
              Raymath.MatrixTranslate(v.Pos.X, 0.25f, v.Pos.Y)
            )


          for mi = 0 to auraMeshes.Length - 1 do
            let struct (mesh, material) = auraMeshes[mi]

            buffer.mesh(mesh, auraTransform, { material with Opacity = 0.35f })
            |> ignore

        // Health bar (only when damaged): black backing + red fill,
        // recorded into the shared billboard batch. Sizes follow the
        // scaled hull.
        if v.Hp < v.MaxHp then
          let frac = float32 v.Hp / float32 v.MaxHp
          let barY = y + 0.35f + 0.55f * scale
          let barW = 0.9f * scale
          let barH = 0.09f * scale

          if barCount + 1 < barCapacity then
            barTextures[barCount] <- tex
            barPositions[barCount] <- Vector3(v.Pos.X, barY, v.Pos.Y)
            barSizes[barCount] <- Vector2(barW, barH)
            barColors[barCount] <- Raylib_cs.Color(0uy, 0uy, 0uy, 200uy)
            barCount <- barCount + 1

          if barCount + 1 < barCapacity then
            barTextures[barCount] <- tex
            barPositions[barCount] <- Vector3(v.Pos.X, barY, v.Pos.Y)
            barSizes[barCount] <- Vector2(barW * frac, barH)
            barColors[barCount] <- Raylib_cs.Color(230uy, 40uy, 40uy, 230uy)
            barCount <- barCount + 1

    InstanceScratch.draw buffer

    // All health bars in one batch (buffer order — drawn after the
    // bodies, so they read on top).
    if barCount > 0 then
      buffer
        .billboardBatch(
          barTextures,
          barPositions,
          barSizes,
          barColors,
          barCount
        )
        .drop()
