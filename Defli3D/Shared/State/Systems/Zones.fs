module Defli3D.State.Systems.Zones

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Zones sub-system — lasting ground effects (slow + damage over
// time), dropped at projectile impact points by Application
// (catapult/cannon/arrow weapons). Purely own-map: rows live and
// expire here; effects are emitted as declarative applications the
// router (Application) translates into Enemies.applyDamage /
// applySlow — zones never touch another system's maps.
//
// Stacking: multiple zones may affect one enemy; per tick each
// enemy accepts at most Def.MaxStacks zone contributions (damage
// adds up, slow takes the STRONGEST factor). Slow is re-applied on
// every zone tick while inside and expires shortly after leaving
// (the Enemies slow-timer machinery already handles expiry).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type ZoneMsg = Drop of drop: struct (Vector2 * ZoneDef)

/// One zone's effect on one enemy this tick — declarative data for
/// the router (damage → applyDamage, slow → applySlow).
[<Struct>]
type ZoneApply = {
  Enemy: int<EnemyId>
  Damage: int
  SlowFactor: float32
  SlowSeconds: float32
}

type ZonesModel() =
  member val Rows = CMap.empty<int<ZoneId>, ZoneRow> with get, set
  member val NextId = 0<ZoneId> with get, set

  /// Tick scratch — the per-enemy stack accumulator, cleared and
  /// reused every tick (steady state allocates nothing).
  member val Scratch =
    Dictionary<int<EnemyId>, struct (int * int * float32 * float32)>() with get, set

module Zones =

  /// Slow expiry horizon: one tick interval after the last
  /// application, so the effect lapses shortly after leaving.
  let private slowSeconds(def: ZoneDef) : float32 = def.TickInterval * 1.5f

  let init() : ZonesModel = ZonesModel()

  /// Cold path: drop a zone at an impact point (Application, from
  /// ProjectileImpact.Zone).
  let handle (msg: ZoneMsg) (model: ZonesModel) : unit =
    match msg with
    | Drop struct (pos, def) ->
      let zid = model.NextId
      model.NextId <- model.NextId + 1<ZoneId>

      model.Rows
      |> CMap.addOrUpdate zid {
        Pos = pos
        Def = def
        Remaining = def.Seconds
        TickTimer = 0f
      }

  /// Hot path: expire rows and tick each zone's damage/slow
  /// application. `enemies` is the Enemies.Alive projection — the
  /// system does ONE internal AMap.getValue (the Towers.tick shape:
  /// a cached version-check read after Towers resolved it earlier
  /// in the same update). Exclusion at the source: a zone only
  /// contributes to enemies its Affects domain covers, so fliers
  /// never receive Ground-zone DoT or slow. The stack accounting is
  /// per-tick (Dictionary reused from a scratch pool — steady state
  /// allocates nothing).
  let tick
    (dt: float32)
    (model: ZonesModel)
    (enemies: amap<int<EnemyId>, EnemyView>)
    : ZoneApply[] =
    let enemies = enemies |> AMap.getValue

    let mutable removes: ResizeArray<int<ZoneId>> = null
    let mutable updates: ResizeArray<struct (int<ZoneId> * ZoneRow)> = null
    // Per-enemy accumulation: (zone contributions, damage sum, best
    // slow factor, that factor's expiry horizon). Owned scratch —
    // Clear keeps the buckets, so steady state allocates nothing.
    let acc = model.Scratch
    acc.Clear()

    for KeyValueV(zid, row) in model.Rows |> AMap.getValue do
      let remaining = row.Remaining - dt

      if remaining <= 0f then
        if isNull removes then
          removes <- ResizeArray()

        removes.Add zid
      else
        let timer = row.TickTimer - dt

        if timer <= 0f then
          // A damage tick: apply to every enemy inside the radius the
          // zone's domain covers, respecting the per-enemy stack cap.
          let radiusSq = row.Def.Radius * row.Def.Radius

          for KeyValueV(eid, v) in enemies do
            if
              Vector2.DistanceSquared(v.Pos, row.Pos) <= radiusSq
              && TargetDomain.covers row.Def.Affects v.Archetype
            then
              let struct (stacks, dmg, slow, slowSecs) =
                acc
                |> Dictionary.tryGetValue eid
                |> ValueOption.defaultValue struct (0, 0, 1f, 0f)

              if stacks < row.Def.MaxStacks then
                let horizon = slowSeconds row.Def

                acc[eid] <-
                  struct (stacks + 1,
                          dmg + row.Def.TickDamage,
                          min slow row.Def.Slow,
                          (if row.Def.Slow <= slow then horizon else slowSecs))

          if isNull updates then
            updates <- ResizeArray()

          updates.Add
            struct (zid,
                    {
                      row with
                          Remaining = remaining
                          TickTimer = row.Def.TickInterval
                    })
        else
          if isNull updates then
            updates <- ResizeArray()

          updates.Add
            struct (zid,
                    {
                      row with
                          Remaining = remaining
                          TickTimer = timer
                    })

    if not(isNull updates) then
      for struct (zid, row) in updates do
        model.Rows |> CMap.addOrUpdate zid row

    if not(isNull removes) then
      Transaction.run(fun () ->
        for zid in removes do
          model.Rows |> CMap.remove zid)

    if acc.Count = 0 then
      Array.empty
    else
      let applies = Array.zeroCreate<ZoneApply> acc.Count
      let mutable i = 0

      for KeyValueV(eid, struct (_, dmg, slow, slowSecs)) in acc do
        // Slow only re-applies while an actual factor is set; damage
        // may be zero (pure-slow zones like the arrow patch).
        applies[i] <- {
          Enemy = eid
          Damage = dmg
          SlowFactor = slow
          SlowSeconds = if slow < 1f then slowSecs else 0f
        }

        i <- i + 1

      applies
