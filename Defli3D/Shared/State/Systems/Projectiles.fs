module Defli3D.State.Systems.Projectiles

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Projectiles sub-system. Owns in-flight shots under the BALLISTIC
// model: fire at a PREDICTED point (Towers' lead solution), fly a
// straight XZ line along Dir; Y follows the trajectory shape (lerp
// muzzle to target, plus the ArcHeight parabola at
// t = Traveled/TotalLen).
//
//   dumbfire (default): never corrects; detonates at (Aim, TargetY)
//     whether or not the enemy is still there. Fast or turning
//     targets genuinely dodge.
//   seek (per HomingPolicy: guns from level 4, rockets always):
//     re-aims Dir at the target's LIVE position each tick (the
//     positions transient read) and Y-homes onto the hull; detonates
//     on arrival. A lost target falls back to the dumbfire leg
//     (aim point).
//   piercing: flies through enemies; each new enemy entering the
//     impact radius takes a direct hit (HitIds prevents re-hits) and
//     the shot only ends on range or lifetime.
//
// Every detonation is an AREA hit (Warhead.ImpactRadius).
// Application applies the damage to all enemies in range and drops
// the lasting Zone when the warhead carries one. Positions are
// logical XZ-plane coordinates in world units.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type ProjectileEvent = Impact of impact: ProjectileImpact

type ProjectilesModel() =
  member val Rows = CMap.empty<int<ProjectileId>, ProjectileRow> with get, set
  member val NextId = 0<ProjectileId> with get, set

module Projectiles =

  let private lifetime = 2.5f
  /// World units. Proximity fuse for seeking shots and pierce
  /// pass-throughs.
  let private hitThreshold = 0.1f

  let init() = ProjectilesModel()

  /// Cold path: spawn one shot (translated by Application from
  /// TowerEvent.Fired, one spawn per volley projectile). The row
  /// embeds the spawn plan; only the live fields (Y, Traveled,
  /// Lifetime, HitIds) are added here.
  let spawn (spawn: ProjectileSpawn) (model: ProjectilesModel) : unit =
    let pid = model.NextId
    model.NextId <- model.NextId + 1<ProjectileId>

    model.Rows
    |> CMap.addOrUpdate pid {
      Spawn = spawn
      Y = spawn.Height
      Traveled = 0f
      Lifetime = lifetime
      HitIds = (if spawn.Warhead.Piercing then ResizeArray() else null)
    }

  /// The flight height at progress t (0..1) for the dumbfire leg:
  /// the muzzle to target lerp plus the arc's parabola.
  let inline private arcY (row: ProjectileRow) (t: float32) : float32 =
    row.Spawn.Height
    + (row.Spawn.TargetY - row.Spawn.Height) * t
    + row.Spawn.ArcHeight * 4f * t * (1f - t)

  /// Hot path: advance every shot one tick (seek re-aim, dumbfire
  /// line, pierce pass-throughs), then impact or expire. `positions`
  /// is a transient read of Enemies.Positions (direct value from the
  /// sim update). Writes are collected and applied after iteration
  /// (transient views die on the next write).
  let tick
    (dt: float32)
    (model: ProjectilesModel)
    (positions: IReadOnlyDictionary<int<EnemyId>, Vector2>)
    : ProjectileEvent seq =
    let mutable events: ResizeArray<ProjectileEvent> = null

    let mutable updates: ResizeArray<struct (int<ProjectileId> * ProjectileRow)> =
      null

    let mutable removes: ResizeArray<int<ProjectileId>> = null

    let impact
      (pid: int<ProjectileId>)
      (pos: Vector2)
      (y: float32)
      (row: ProjectileRow)
      =
      if isNull events then
        events <- ResizeArray()

      events.Add(
        Impact {
          Projectile = pid
          Enemy = ValueNone
          Pos = pos
          Y = y
          Warhead = row.Spawn.Warhead
        }
      )

      if isNull removes then
        removes <- ResizeArray()

      removes.Add pid

    for KeyValueV(pid, row) in model.Rows |> AMap.getValue do
      let plan = row.Spawn
      let lifetime = row.Lifetime - dt

      if lifetime <= 0f then
        if isNull removes then
          removes <- ResizeArray()

        removes.Add pid
      else
        let step = plan.Speed * dt

        // ── Seek leg: chase the live target (falls back to the aim
        // point once it despawns; the shot still arrives) ──
        let seekPos =
          if plan.Seek then
            match plan.Target with
            | ValueSome eid ->
              positions
              |> ReadOnlyDict.tryGetValue eid
              |> ValueOption.defaultValue plan.Aim
            | ValueNone -> plan.Aim
          else
            plan.Aim

        let chasing = plan.Seek

        let mutable pos' = plan.Pos
        let mutable dir' = plan.Dir
        let mutable traveled' = row.Traveled
        let mutable total' = plan.TotalLen
        let mutable y' = row.Y
        let mutable detonated = false

        if chasing then
          let d = seekPos - plan.Pos
          let dist = d.Length()

          if dist <= step + hitThreshold then
            // Arrived ON the target: detonate at its hull.
            detonated <- true
            impact pid seekPos plan.TargetY row
          else
            dir' <- d / dist
            pos' <- plan.Pos + dir' * step
            traveled' <- row.Traveled + step
            total' <- plan.TotalLen + step
            // Y-homing: cover the same fraction of the height gap the
            // XZ chase covers this tick.
            y' <- row.Y + (plan.TargetY - row.Y) * min 1f (step / dist)
        else
          // ── Dumbfire leg: straight line to the aim point ──
          traveled' <- row.Traveled + step

          if traveled' >= plan.TotalLen then
            detonated <- true
            impact pid plan.Aim plan.TargetY row
          else
            let t = min 1f (traveled' / plan.TotalLen)
            pos' <- plan.Pos + plan.Dir * step
            y' <- arcY row t

        // ── Pierce pass-throughs: direct hits on new enemies near
        // the flight line (the shot keeps flying) ──
        if plan.Warhead.Piercing && not detonated && not(isNull row.HitIds) then
          let radiusSq = plan.Warhead.ImpactRadius * plan.Warhead.ImpactRadius

          for KeyValueV(eid, epos) in positions do
            if
              Vector2.DistanceSquared(epos, pos') <= radiusSq
              && not(row.HitIds.Contains eid)
            then
              row.HitIds.Add eid

              if isNull events then
                events <- ResizeArray()

              // Direct hit (no area, no zone). The piercer's
              // per-enemy hit list is the full effect.
              events.Add(
                Impact {
                  Projectile = pid
                  Enemy = ValueSome eid
                  Pos = epos
                  Y = y'
                  Warhead = {
                    plan.Warhead with
                        ImpactRadius = 0f
                        Zone = ValueNone
                  }
                }
              )

        if not detonated then
          if isNull updates then
            updates <- ResizeArray()

          updates.Add
            struct (pid,
                    {
                      row with
                          Spawn = {
                            plan with
                                Pos = pos'
                                Dir = dir'
                                TotalLen = total'
                          }
                          Y = y'
                          Traveled = traveled'
                          Lifetime = lifetime
                    })

    if not(isNull updates) then
      for struct (pid, row) in updates do
        model.Rows |> CMap.addOrUpdate pid row

    if not(isNull removes) then
      Transaction.run(fun () ->
        for pid in removes do
          model.Rows |> CMap.remove pid)

    if isNull events then Array.empty else events
