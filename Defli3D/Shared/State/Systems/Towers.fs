module Defli3D.State.Systems.Towers

open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli3D.State
open Defli3D

// ─────────────────────────────────────────────────────────────
// Towers sub-system — owns placement, targeting, firing.
//
//   Statics   — { Def, Cell } written once at placement
//   Runtimes  — { Cooldown, Target } written every tick
//   CellIndex — cell → tower id (placement occupancy + the
//               RangeRing projection's hover lookup)
//
// Targeting reads the Enemies.Alive TRANSIENT VIEW passed in as a
// direct value by the sim update (hot path, no closures). Phase 3 adds
// the TargetPolicy field; Phase 2 always picks "first" (the enemy
// closest to the base — highest progress).
//
// 1 cell = 1 world unit: the def's Range IS the world-space range
// (Defli multiplied by the 64 px cell size).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type TowerMsg =
  | Place of struct (struct (int * int) * TowerDef)
  /// Cold path: bump the tower's level (Application validates gold
  /// and the cap before sending).
  | Upgrade of tower: int<TowerId>

[<Struct>]
type TowerEvent = Fired of shot: TowerShot

type TowersModel() =
  member val Statics = CMap.empty<int<TowerId>, TowerStatic> with get, set
  member val Runtimes = CMap.empty<int<TowerId>, TowerRuntime> with get, set

  member val CellIndex =
    CMap.empty<struct (int * int), int<TowerId>> with get, set

  /// Upgrade level per tower (1 = base def) — a SEPARATE component map
  /// so the EffectiveDef projection composes on top of Statics
  /// (Phase 5 projection-composition showcase).
  member val Levels = CMap.empty<int<TowerId>, int> with get, set

  /// Tagged from the start — ids never pass through a plain int.
  member val NextId = 0<TowerId> with get, set

  /// The EFFECTIVE def per tower: Statics.Def × Levels — a same-key
  /// AMap.joinOn (the per-tower subgraph swaps its static input in
  /// place, no rebuild on write); the missing level falls back to 1.
  /// RangeRing composes on top of this, the tick reads it transiently
  /// once per frame.
  member val EffectiveDef: amap<int<TowerId>, TowerDef> =
    Unchecked.defaultof<_> with get, set

module Towers =

  let inline private buildEffectiveDef
    (m: TowersModel)
    : amap<int<TowerId>, TowerDef> =
    AMap.joinOn m.Statics m.Levels (fun tid _ -> tid) (fun _ staticV levelV ->
      AVal.map2
        (fun (s: TowerStatic) (level: int voption) ->
          ValueSome(
            TowerDefs.effectiveDef s.Def (level |> ValueOption.defaultValue 1)
          ))
        staticV
        levelV)

  let init() : TowersModel =
    let m = TowersModel()
    m.EffectiveDef <- buildEffectiveDef m
    m

  /// Cold path: place a tower. Application validates (buildable tile,
  /// occupancy, gold) before sending — this only writes the rows.
  let handle (msg: TowerMsg) (model: TowersModel) : unit =
    match msg with
    | Place(cell, def) ->
      let tid = model.NextId
      model.NextId <- model.NextId + 1<TowerId>

      Transaction.run(fun () ->
        model.Statics |> CMap.addOrUpdate tid { Def = def; Cell = cell }

        model.Runtimes
        |> CMap.addOrUpdate tid {
          Cooldown = 0f
          Target = ValueNone
          Aim = ValueNone
        }

        model.CellIndex |> CMap.addOrUpdate cell tid)
    | Upgrade tid ->
      let level =
        model.Levels |> CMap.tryGetValue tid |> ValueOption.defaultValue 1

      model.Levels |> CMap.addOrUpdate tid (level + 1)

  /// Hot path: cooldown decay + target acquisition + the lead-
  /// prediction firing solution. `alive` is a transient read of
  /// Enemies.Alive, `velocities` one of Enemies.Velocities (plain
  /// rows measured by the movement tick — the prediction input), and
  /// `suppression` one of the state's boss-aura projection (all
  /// direct values from the sim update — hot path, no closures);
  /// `cellSize` is the grid's uniform cell size (Vector2(1, 1) —
  /// 1 cell = 1 world unit).
  let tick
    (dt: float32)
    (model: TowersModel)
    (alive: amap<int<EnemyId>, EnemyView>)
    (velocities: IReadOnlyDictionary<int<EnemyId>, Vector2>)
    (suppression: IReadOnlyDictionary<int<TowerId>, float32>)
    (cellSize: Vector2)
    : TowerEvent seq =
    let mutable events: ResizeArray<TowerEvent> = null

    // ONE transient read of the composed projection per frame — the
    // effective def (Statics × Levels) drives range/damage/rate/policy.
    let effective = model.EffectiveDef |> AMap.getValue
    // One transient alive view for the whole loop (targeting + the
    // held target's live aim position).
    let aliveView = alive |> AMap.getValue

    for KeyValueV(tid, s) in model.Statics |> AMap.getValue do
      let def =
        effective
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue s.Def

      // Boss aura (Phase 6): a live boss near this tower multiplies
      // its fire rate by the suppression factor (default 1 = free).
      let suppress =
        suppression
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue 1f

      let center = Cells.center s.Cell cellSize
      // 1 cell = 1 world unit: the def's Range IS the world-space range.
      let rangeWorld = float32 def.Range
      let runtimes = model.Runtimes |> AMap.tryFind tid
      let targetA = runtimes |> AVal.map(ValueOption.bind _.Target)

      let cooldownA =
        runtimes
        |> AVal.map(ValueOption.map _.Cooldown >> ValueOption.defaultValue 0f)

      let cooldown = cooldownA |> AVal.getValue

      let cooldown' = max 0f (cooldown - dt)

      if cooldown' <= 0f then
        // Acquire a target: in range + exact distance, then the def's
        // policy decides among the candidates (Phase 3).
        let mutable best: struct (int<EnemyId> * EnemyView * float32) voption =
          ValueNone

        for KeyValueV(eid, v) in aliveView do
          let d = Vector2.Distance(center, v.Pos)

          if d <= rangeWorld && TargetDomain.covers def.Targets v.Archetype then
            let better =
              match best with
              | ValueNone -> true
              | ValueSome struct (_, bv, bd) ->
                match def.TargetPolicy with
                | TargetPolicy.First -> v.Progress > bv.Progress
                | TargetPolicy.Last -> v.Progress < bv.Progress
                | TargetPolicy.Strongest -> v.MaxHp > bv.MaxHp
                | TargetPolicy.Weakest -> v.Hp < bv.Hp
                | TargetPolicy.Closest -> d < bd

            if better then
              best <- ValueSome struct (eid, v, d)

        match best with
        | ValueSome struct (eid, v, _) ->
          if isNull events then
            events <- ResizeArray()

          // Seek resolves from the def's policy × the tower's level:
          // rockets always chase, guns chase from level 4, loaders
          // (ballistas/cannons/catapults) NEVER — their ammo is a
          // dumb chunk that only gets the lead prediction.
          let level =
            model.Levels |> CMap.tryGetValue tid |> ValueOption.defaultValue 1

          let seek =
            match def.Homing with
            | HomingPolicy.Always -> true
            | HomingPolicy.FromLevel n -> level >= n
            | HomingPolicy.Never -> false

          // Lead prediction: aim where the target WILL be — its
          // velocity × the shot's flight time (distance / projectile
          // speed, one iteration). Dumbfire shots never correct after
          // this; a target that changes speed or direction genuinely
          // dodges.
          let flight =
            Vector2.Distance(center, v.Pos) / max 0.001f def.ProjectileSpeed

          let vel =
            velocities
            |> ReadOnlyDict.tryGetValue eid
            |> ValueOption.defaultValue Vector2.Zero

          let aim = v.Pos + vel * flight

          // The muzzle's world XZ: offset from the tower center
          // along the firing line (the gun's barrel end / the deck's
          // embrasure) — shots and muzzle VFX leave the barrel, not
          // the tower's middle.
          let line = aim - center
          let lineLen = line.Length()

          let aimDir =
            if lineLen > 0.0001f then line / lineLen else Vector2.UnitX

          let muzzle =
            center
            + aimDir * (TowerLayout.muzzleReach def * TowerLayout.towerScale)

          events.Add(
            Fired {
              Tower = tid
              Enemy = ValueSome eid
              Aim = aim
              Muzzle = muzzle
              Damage = def.Damage
              ImpactRadius = def.ImpactRadius
              Piercing = def.Piercing
              Seek = seek
              Volley = def.Volley
              Spread = def.Spread
              Trajectory = def.Trajectory
              Zone = def.Zone
              ProjectileModel = def.ProjectileModel
              ProjectileScale = def.ProjectileScale
              Height = TowerLayout.muzzleY s.Def
              MuzzleDust = def.MuzzleDust
            }
          )

          // Cooldown from the EFFECTIVE def (Statics × Levels): the
          // +10 %/level fire-rate upgrade must actually apply. The
          // boss-aura suppression factor multiplies the rate (0.5 =
          // half speed → double cooldown). Aim carries the target's
          // live position — the TowerAim projection feeds the
          // rotating chassis.
          model.Runtimes
          |> CMap.addOrUpdate tid {
            Cooldown = 1f / max 0.1f (def.FireRate * suppress)
            Target = ValueSome eid
            Aim = ValueSome v.Pos
          }
        | ValueNone ->
          model.Runtimes
          |> CMap.addOrUpdate tid {
            Cooldown = 0f
            Target = ValueNone
            Aim = ValueNone
          }
      else
        let target = targetA |> AVal.getValue

        // Aim tracks the held target's live position while the
        // cooldown runs (rotating chassis keep pointing at it).
        let aim =
          target
          |> ValueOption.bind(fun eid ->
            aliveView |> ReadOnlyDict.tryGetValue eid)
          |> ValueOption.map(_.Pos)

        model.Runtimes
        |> CMap.addOrUpdate tid {
          Cooldown = cooldown'
          Target = target
          Aim = aim
        }

    if isNull events then Array.empty else events
