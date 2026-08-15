module Defli3D.State.Systems.Projectiles

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Projectiles sub-system — owns in-flight shots under the BALLISTIC
// model: fire at a PREDICTED point (Towers' lead solution), fly a
// straight XZ line along Dir, Y follows the trajectory shape
// (lerp muzzle→target + ArcHeight·4t(1−t), t = Traveled/TotalLen).
//
//   dumbfire (default) — never corrects; detonates at (Aim, TargetY)
//     whether or not the enemy is still there. Fast or turning
//     targets genuinely dodge.
//   seek (per HomingPolicy: guns from level 4, rockets always) —
//     re-aims Dir at the target's LIVE position each tick (the
//     positions transient read) and Y-homes onto the hull; detonates
//     on arrival. A lost target falls back to the dumbfire leg
//     (aim point).
//   piercing — flies THROUGH enemies: each new enemy entering the
//     impact radius takes a direct hit (HitIds prevents re-hits) and
//     the shot only ends on range/lifetime.
//
// Every detonation is an AREA hit (ImpactRadius) — Application fans
// the damage and drops the lasting Zone when the weapon carries one.
// Positions are logical XZ-plane coordinates in world units.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type ProjectileMsg = Spawn of spawn: ProjectileSpawn

[<Struct>]
type ProjectileEvent = Impact of impact: ProjectileImpact

type ProjectilesModel() =
  member val Rows = CMap.empty<int<ProjectileId>, ProjectileRow> with get, set
  member val NextId = 0<ProjectileId> with get, set

module Projectiles =

  let private lifetime = 2.5f
  /// World units — proximity fuse for seeking shots and pierce
  /// pass-throughs.
  let private hitThreshold = 0.1f

  let init() = ProjectilesModel()

  /// Cold path: spawn one shot (translated by Application from
  /// TowerEvent.Fired — one spawn per volley projectile).
  let handle (msg: ProjectileMsg) (model: ProjectilesModel) : unit =
    match msg with
    | Spawn spawn ->
      let pid = model.NextId
      model.NextId <- model.NextId + 1<ProjectileId>

      model.Rows
      |> CMap.addOrUpdate pid {
        Pos = spawn.Pos
        Y = spawn.Height
        Dir = spawn.Dir
        Speed = spawn.Speed
        Traveled = 0f
        TotalLen = spawn.TotalLen
        MuzzleY = spawn.Height
        TargetY = spawn.TargetY
        ArcHeight = spawn.ArcHeight
        Seek = spawn.Seek
        Target = spawn.Target
        Aim = spawn.Aim
        Damage = spawn.Damage
        ImpactRadius = spawn.ImpactRadius
        Piercing = spawn.Piercing
        HitIds = (if spawn.Piercing then ResizeArray() else null)
        Zone = spawn.Zone
        Lifetime = lifetime
        Model = spawn.Model
        Scale = spawn.Scale
      }

  /// The flight height at progress t (0..1) for the dumbfire leg:
  /// the muzzle→target lerp plus the arc's parabola.
  let inline private arcY (row: ProjectileRow) (t: float32) : float32 =
    row.MuzzleY
    + (row.TargetY - row.MuzzleY) * t
    + row.ArcHeight * 4f * t * (1f - t)

  /// Hot path: advance every shot one tick — seek re-aim / dumbfire
  /// line / pierce pass-throughs — impact or expire. `positions` is a
  /// transient read of Enemies.Positions (direct value from the sim
  /// update). Writes are collected and applied after iteration
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
          Damage = row.Damage
          ImpactRadius = row.ImpactRadius
          Zone = row.Zone
        }
      )

      if isNull removes then
        removes <- ResizeArray()

      removes.Add pid

    for KeyValueV(pid, row) in model.Rows |> AMap.getValue do
      let lifetime = row.Lifetime - dt

      if lifetime <= 0f then
        if isNull removes then
          removes <- ResizeArray()

        removes.Add pid
      else
        let step = row.Speed * dt

        // ── Seek leg: chase the live target (falls back to the aim
        // point once it despawns — the shot still arrives) ──
        let seekPos =
          if row.Seek then
            match row.Target with
            | ValueSome eid ->
              positions
              |> ReadOnlyDict.tryGetValue eid
              |> ValueOption.defaultValue row.Aim
            | ValueNone -> row.Aim
          else
            row.Aim

        let chasing = row.Seek

        let mutable pos' = row.Pos
        let mutable dir' = row.Dir
        let mutable traveled' = row.Traveled
        let mutable total' = row.TotalLen
        let mutable y' = row.Y
        let mutable detonated = false

        if chasing then
          let d = seekPos - row.Pos
          let dist = d.Length()

          if dist <= step + hitThreshold then
            // Arrived ON the target: detonate at its hull.
            detonated <- true
            impact pid seekPos row.TargetY row
          else
            dir' <- d / dist
            pos' <- row.Pos + dir' * step
            traveled' <- row.Traveled + step
            total' <- row.TotalLen + step
            // Y-homing: cover the same fraction of the height gap the
            // XZ chase covers this tick.
            y' <- row.Y + (row.TargetY - row.Y) * min 1f (step / dist)
        else
          // ── Dumbfire leg: straight line to the aim point ──
          traveled' <- row.Traveled + step

          if traveled' >= row.TotalLen then
            detonated <- true
            impact pid row.Aim row.TargetY row
          else
            let t = min 1f (traveled' / row.TotalLen)
            pos' <- row.Pos + row.Dir * step
            y' <- arcY row t

        // ── Pierce pass-throughs: direct hits on new enemies near
        // the flight line (the shot keeps flying) ──
        if row.Piercing && not detonated && not(isNull row.HitIds) then
          for KeyValueV(eid, epos) in positions do
            if
              Vector2.DistanceSquared(epos, pos')
              <= row.ImpactRadius * row.ImpactRadius
              && not(row.HitIds.Contains eid)
            then
              row.HitIds.Add eid

              if isNull events then
                events <- ResizeArray()

              // Direct hit (no area fan) — the piercer's damage list
              // IS the fan-out.
              events.Add(
                Impact {
                  Projectile = pid
                  Enemy = ValueSome eid
                  Pos = epos
                  Y = y'
                  Damage = row.Damage
                  ImpactRadius = 0f
                  Zone = ValueNone
                }
              )

        if not detonated then
          if isNull updates then
            updates <- ResizeArray()

          updates.Add
            struct (pid,
                    {
                      row with
                          Pos = pos'
                          Y = y'
                          Dir = dir'
                          Traveled = traveled'
                          TotalLen = total'
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
