module Defli3D.State.Systems.Projectiles

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Projectiles sub-system — owns in-flight shots. One map is enough
// (projectiles have no cross-component reads). Its render position
// is the state-owned Homing projection (Projectiles.Rows ×
// Enemies.Positions — see Projections.fs).
//
// The homing feel: the projectile seeks the target's LIVE position
// row each tick (passed in as a direct transient read of
// Enemies.Positions — hot path, no closures). A target that despawns
// mid-flight leaves the shot seeking its LastTargetPos instead: it
// detonates there rather than vanishing mid-air.
//
// The 3D homing: each tick the shot also integrates Y toward the
// target's hull-center height (TargetY — frozen at fire time from
// EnemyLayout.impactY) in lockstep with the XZ seek: the same
// step/dist fraction of the height gap, so the shell arrives AT the
// hull when the seek arrives — no more detonating at muzzle height
// in the air beside the target. XZ seek logic unchanged.
//
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
  /// World units (Defli's 6 px ÷ 64, rounded).
  let private hitThreshold = 0.1f

  let init() = ProjectilesModel()

  /// Cold path: spawn one shot (translated by Application from TowerEvent.Fired).
  let handle (msg: ProjectileMsg) (model: ProjectilesModel) : unit =
    match msg with
    | Spawn spawn ->
      let pid = model.NextId
      model.NextId <- model.NextId + 1<ProjectileId>

      model.Rows
      |> CMap.addOrUpdate pid {
        Pos = spawn.Pos
        Y = spawn.Height
        TargetY = spawn.TargetY
        TargetEnemy = spawn.TargetEnemy
        LastTargetPos = spawn.LastTargetPos
        Damage = spawn.Damage
        Speed = spawn.Speed
        Lifetime = lifetime
        SlowFactor = spawn.SlowFactor
        SlowSeconds = spawn.SlowSeconds
        SplashRadius = spawn.SplashRadius
        ProjectileModel = spawn.ProjectileModel
      }

  /// Hot path: advance toward the target's live position — XZ seek
  /// plus Y homing toward the hull center (TargetY) — impact or
  /// expire. `positions` is a transient read of Enemies.Positions
  /// (direct value from the sim update). A target that despawns mid-flight
  /// no longer removes the shot: it flies on to the target's LAST
  /// RECORDED position and detonates there — no mid-air pop, and a
  /// splash shell still blasts the pack around the corpse (the impact
  /// carries the dead id; Application's splash fan-out hits whatever is
  /// actually near the point). Writes are collected and applied after
  /// iteration (transient views die on the next write).
  let tick
    (dt: float32)
    (model: ProjectilesModel)
    (positions: IReadOnlyDictionary<int<EnemyId>, Vector2>)
    : ProjectileEvent seq =
    let mutable events: ResizeArray<ProjectileEvent> = null

    let mutable updates: ResizeArray<struct (int<ProjectileId> * ProjectileRow)> =
      null

    let mutable removes: ResizeArray<int<ProjectileId>> = null

    for KeyValueV(pid, row) in model.Rows |> AMap.getValue do
      let lifetime = row.Lifetime - dt

      if lifetime <= 0f then
        if isNull removes then
          removes <- ResizeArray()

        removes.Add pid
      else
        // Live position while the target lives, last recorded after.
        let struct (targetPos, live) =
          positions
          |> ReadOnlyDict.tryGetValue row.TargetEnemy
          |> ValueOption.map(fun p -> struct (p, true))
          |> ValueOption.defaultValue struct (row.LastTargetPos, false)

        let d = targetPos - row.Pos
        let dist = d.Length()
        let step = row.Speed * dt

        if dist <= step + hitThreshold then
          if isNull events then
            events <- ResizeArray()

          events.Add(
            Impact {
              Projectile = pid
              Enemy = row.TargetEnemy
              Damage = row.Damage
              Pos = row.Pos
              Y = row.Y
              SlowFactor = row.SlowFactor
              SlowSeconds = row.SlowSeconds
              SplashRadius = row.SplashRadius
            }
          )

          if isNull removes then
            removes <- ResizeArray()

          removes.Add pid
        else
          // Y-homing: cover the same fraction of the height gap the
          // XZ seek covers this tick, so the shell arrives at the
          // hull center when the seek arrives. This branch only runs
          // while dist > step + hitThreshold, so step/dist < 1 — the
          // min 1f guard is for degenerate cases only.
          let y' = row.Y + (row.TargetY - row.Y) * min 1f (step / dist)

          if isNull updates then
            updates <- ResizeArray()

          updates.Add
            struct (pid,
                    {
                      row with
                          Pos = row.Pos + (d / dist) * step
                          Y = y'
                          Lifetime = lifetime
                          LastTargetPos =
                            if live then targetPos else row.LastTargetPos
                    })

    if not(isNull updates) then
      for struct (pid, row) in updates do
        model.Rows |> CMap.addOrUpdate pid row

    if not(isNull removes) then
      Transaction.run(fun () ->
        for pid in removes do
          model.Rows |> CMap.remove pid)

    if isNull events then Array.empty else events
