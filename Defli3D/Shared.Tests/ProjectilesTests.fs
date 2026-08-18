module Defli3D.Tests.ProjectilesTests

open System.Collections.Generic
open System.Numerics
open Expecto
open Mibo.Adaptive
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open TestData
open Defli3D.State.Systems.Projectiles
open Defli3D.State.Systems.Enemies

let private cfg = TestData.Fixtures.cfg
let private map = MapModel.create cfg
let private model() = Projectiles.init()

let private target = 0<EnemyId>

/// A transient Positions-shaped dict with one enemy at pos.
let private positionsAt(pos: Vector2) =
  let d = Dictionary<int<EnemyId>, Vector2>()
  d[target] <- pos
  d

let private noPositions = Dictionary<int<EnemyId>, Vector2>()

/// Spawns one shot from pos to aim (flat, radius 0.25, damage 5) —
/// optionally seeking `target`.
let private spawnShot
  (m: ProjectilesModel)
  (pos: Vector2)
  (aim: Vector2)
  (seek: bool)
  =
  let d = aim - pos
  let len = d.Length()

  Projectiles.spawn
    {
      Pos = pos
      Height = 0.3f
      TargetY = 0.3f
      Dir = d / len
      TotalLen = len
      ArcHeight = 0f
      Seek = seek
      Target = if seek then ValueSome target else ValueNone
      Aim = aim
      Warhead = {
        Damage = 5f
        ImpactRadius = 0.25f
        Piercing = false
        Zone = ValueNone
      }
      Model = Models.ammoBullet
      Scale = 0.7f
      Speed = 1.5625f // 100 px/s ÷ 64
    }
    m

  m

let tests =
  testList "Projectiles" [
    testCase "spawn adds a row" (fun () ->
      let m = model()
      spawnShot m (Vector2.Zero) (Vector2(0.78125f, 0f)) false |> ignore

      Expect.equal ((m.Rows |> AMap.getValue).Count) 1 "one row"

      match m.Rows |> CMap.tryGetValue(0<ProjectileId>) with
      | ValueSome row ->
        Expect.equal row.Spawn.Warhead.Damage 5f "damage"
        Expect.equal row.Y 0.3f "Y seeded from the spawn height"
        Expect.equal row.Spawn.Seek false "dumbfire by default"
        Expect.equal row.Spawn.Dir (Vector2(1f, 0f)) "flight direction"
      | ValueNone -> failtest "row must exist")

    testCase
      "dumbfire: flies the straight line and detonates AT the aim point"
      (fun () ->
        let mutable m = model()
        m <- spawnShot m (Vector2(0f, 0f)) (Vector2(0.78125f, 0f)) false

        // The target is NOT at the aim point (it dodged) — the shot
        // does not care: no positions needed at all.
        let _ = Projectiles.tick 0.1f m noPositions

        match m.Rows |> CMap.tryGetValue(0<ProjectileId>) with
        | ValueSome row ->
          Expect.equal row.Spawn.Pos.X 0.15625f "step along the fixed line"
          Expect.equal row.Traveled 0.15625f "progress recorded"
        | ValueNone -> failtest "row must exist"

        let events = Projectiles.tick 1.0f m noPositions

        match events |> Seq.toArray with
        | [| Impact impact |] ->
          Expect.equal impact.Enemy ValueNone "area detonation"
          Expect.equal impact.Pos (Vector2(0.78125f, 0f)) "at the aim point"
          Expect.equal impact.Warhead.Damage 5f "damage"
          Expect.equal impact.Warhead.ImpactRadius 0.25f "radius"
        | _ -> failtest "expected exactly one Impact"

        Expect.equal ((m.Rows |> AMap.getValue).Count) 0 "removed on impact")

    testCase "arc: Y follows the muzzle→target lerp + parabola" (fun () ->
      let mutable m = model()

      // Flat lerp base 0.3 → 0.3, arc apex 0.8: at t = 0.5 the
      // parabola adds 4·t·(1−t)·0.8 = 0.8.
      let d = Vector2(1.5625f, 0f)

      Projectiles.spawn
        {
          Pos = Vector2.Zero
          Height = 0.3f
          TargetY = 0.3f
          Dir = Vector2(1f, 0f)
          TotalLen = 1.5625f
          ArcHeight = 0.8f
          Seek = false
          Target = ValueNone
          Aim = d
          Warhead = {
            Damage = 5f
            ImpactRadius = 0.25f
            Piercing = false
            Zone = ValueNone
          }
          Model = Models.ammoCannonball
          Scale = 1f
          Speed = 1.5625f
        }
        m

      // 0.5 s at 1.5625 u/s → traveled 0.78125 = t 0.5 of 1.5625.
      let _ = Projectiles.tick 0.5f m noPositions

      match m.Rows |> CMap.tryGetValue(0<ProjectileId>) with
      | ValueSome row -> Expect.equal row.Y 1.1f "arc apex reached"
      | ValueNone -> failtest "row must exist")

    testCase "seek: chases the live target and impacts on the hull" (fun () ->
      let mutable m = model()
      // Fired at a predicted point, but the shot seeks and the live
      // target stands closer — it re-aims.
      m <- spawnShot m (Vector2(0f, 0f)) (Vector2(0.78125f, 0f)) true

      let _ = Projectiles.tick 0.1f m (positionsAt(Vector2(0.5f, 0f)))

      match m.Rows |> CMap.tryGetValue(0<ProjectileId>) with
      | ValueSome row ->
        Expect.equal row.Spawn.Pos.X 0.15625f "chases the live position"
        Expect.isTrue (row.Spawn.Dir.X > 0.99f) "re-aimed"
      | ValueNone -> failtest "row must exist"

      let events = Projectiles.tick 1.0f m (positionsAt(Vector2(0.5f, 0f)))

      match events |> Seq.toArray with
      | [| Impact impact |] ->
        Expect.equal impact.Pos (Vector2(0.5f, 0f)) "arrived on the target"
      | _ -> failtest "expected exactly one Impact"

      Expect.equal ((m.Rows |> AMap.getValue).Count) 0 "removed on impact")

    testCase "seek: a lost target falls back to the aim point" (fun () ->
      let mutable m = model()
      m <- spawnShot m (Vector2(0f, 0f)) (Vector2(0.78125f, 0f)) true

      // Target despawns: the chase falls back to the aim point — the
      // shot still arrives (no mid-air pop).
      let events = Projectiles.tick 1.0f m noPositions

      match events |> Seq.toArray with
      | [| Impact impact |] ->
        Expect.equal impact.Pos (Vector2(0.78125f, 0f)) "arrived at the aim"
      | _ -> failtest "expected exactly one Impact")

    testCase
      "piercing: direct hits in passing, then detonates at the end"
      (fun () ->
        let mutable m = model()

        let d = Vector2(1.5625f, 0f)

        Projectiles.spawn
          {
            Pos = Vector2.Zero
            Height = 0.3f
            TargetY = 0.3f
            Dir = Vector2(1f, 0f)
            TotalLen = 1.5625f
            ArcHeight = 0f
            Seek = false
            Target = ValueNone
            Aim = d
            Warhead = {
              Damage = 7f
              ImpactRadius = 0.4f
              Piercing = true
              Zone = ValueNone
            }
            Model = Models.ammoArrow
            Scale = 1.4f
            Speed = 1.5625f
          }
          m

        // The enemy sits ON the flight line at x = 0.3.
        let positions = positionsAt(Vector2(0.3f, 0f))

        // First tick (step 0.15625): the enemy at 0.3 is within 0.4 of
        // the new position → ONE direct hit; the shot keeps flying.
        let events = Projectiles.tick 0.1f m positions

        match events |> Seq.toArray with
        | [| Impact hit |] ->
          Expect.equal hit.Enemy (ValueSome target) "direct pass-through hit"
          Expect.equal hit.Warhead.Damage 7f "pierce damage"

          Expect.equal
            hit.Warhead.ImpactRadius
            0f
            "no area fan on pass-through"
        | _ -> failtest "expected exactly one pass-through hit"

        Expect.equal ((m.Rows |> AMap.getValue).Count) 1 "still flying"

        // Next tick passes the SAME enemy again — HitIds blocks the
        // re-hit.
        let events2 = Projectiles.tick 0.1f m positions

        Expect.isEmpty events2 "no re-hit"

        // Fly out to the end of the line: the final detonation.
        let events3 = Projectiles.tick 1.0f m positions

        match events3 |> Seq.toArray with
        | [| Impact det |] ->
          Expect.equal det.Enemy ValueNone "end-of-line detonation"
          Expect.equal det.Pos d "at the aim point"
        | _ -> failtest "expected the final detonation"

        Expect.equal ((m.Rows |> AMap.getValue).Count) 0 "removed at the end")

    testCase "spawn carries the zone payload to Impact" (fun () ->
      let mutable m = model()

      let zone = {
        Radius = 1.3f
        Seconds = 4f
        Slow = 0.6f
        TickDamage = 4f
        TickInterval = 0.5f
        MaxStacks = 5
        Affects = TargetDomain.Ground
      }

      let d = Vector2(0.3f, 0f)

      Projectiles.spawn
        {
          Pos = Vector2.Zero
          Height = 0.3f
          TargetY = 0.3f
          Dir = Vector2(1f, 0f)
          TotalLen = 0.3f
          ArcHeight = 0f
          Seek = false
          Target = ValueNone
          Aim = d
          Warhead = {
            Damage = 40f
            ImpactRadius = 1.3f
            Piercing = false
            Zone = ValueSome zone
          }
          Model = Models.ammoBoulder
          Scale = 1f
          Speed = 1.5625f
        }
        m

      let events = Projectiles.tick 1.0f m noPositions

      match events |> Seq.toArray with
      | [| Impact impact |] ->
        Expect.equal impact.Warhead.Zone (ValueSome zone) "zone payload"
        Expect.equal impact.Warhead.ImpactRadius 1.3f "big radius"
      | _ -> failtest "expected exactly one Impact")

    testCase "lifetime expiry removes the row" (fun () ->
      let mutable m = model()
      m <- spawnShot m (Vector2(0f, 0f)) (Vector2(99f, 0f)) false

      for _ in 1..30 do
        let events = Projectiles.tick 0.1f m noPositions
        Expect.isEmpty events "must not impact"

      Expect.equal ((m.Rows |> AMap.getValue).Count) 0 "expired")

    testCase
      "Homing projection: render rows carry Pos/Y/Dir/Model/Scale"
      (fun () ->
        let enemies = Enemies.Enemies.init()
        let projectiles = model()

        spawnShot projectiles (Vector2(0f, 0f)) (Vector2(0.78125f, 0f)) false
        |> ignore

        let towers = Towers.Towers.init()
        let economy = Economy.Economy.init cfg
        let hover = CVal.create ValueNone

        let projections =
          Projections(
            enemies,
            towers,
            projectiles,
            economy,
            MapModel.buildableGrid map,
            hover,
            CVal.create TowerDefs.sentry
          )

        let rows = projections.Homing |> AMap.getValue
        Expect.equal rows.Count 1 "one homing row"

        for KeyValueV(_, v) in rows do
          Expect.equal v.Pos Vector2.Zero "projectile pos"
          Expect.equal v.Y 0.3f "flight height"
          Expect.equal v.Dir (Vector2(1f, 0f)) "flight direction"
          Expect.equal v.Scale 0.7f "view scale")
  ]
