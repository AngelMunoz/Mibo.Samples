module Defli3D.Tests.ProjectionTests

open System.Numerics
open Expecto
open Mibo.Adaptive
open TestData
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Enemies
open Defli3D.State.Systems.Economy

// ─────────────────────────────────────────────────────────────
// The ECS "query returns what the tables contain" contract: the
// reactive projections must agree with the component maps at
// every step. These are the AdaptiveSlop stress assertions.
// ─────────────────────────────────────────────────────────────

let private cfg = Fixtures.cfg
let private map = MapModel.create cfg

let private aliveView(m: EnemiesModel) = m.Alive |> AMap.getValue

let private viewsView(m: EnemiesModel) = m.Views |> AMap.getValue

let private spawn (m: EnemiesModel) (def: EnemyDef) =
  let _ = Enemies.handle (EnemyMsg.Spawn def) m map.Path
  let m' = m
  m'

let tests =
  testList "Projections" [
    testCase "Views joins all three component maps" (fun () ->
      let m = spawn (Enemies.init()) Fixtures.grunt
      let views = viewsView m
      Expect.equal views.Count 1 "one row"

      for KeyValueV(eid, v) in views do
        Expect.equal v.Pos map.Path[0] "pos from Positions"
        Expect.equal v.Hp Fixtures.grunt.Hp "hp from Healths"
        Expect.equal v.MaxHp Fixtures.grunt.Hp "maxHp from Healths"
        Expect.equal v.Progress 0f "progress from Motions"
        Expect.equal v.Slow 1f "slow from Motions")

    testCase "damage delta lands on exactly the damaged enemy" (fun () ->
      let mutable m = Enemies.init()
      m <- spawn m Fixtures.grunt // id 0
      m <- spawn m Fixtures.tank // id 1
      m <- spawn m Fixtures.runner // id 2

      // Damage the tank (id 1).
      let _ = Enemies.handle (EnemyMsg.ApplyDamage(1<EnemyId>, 50)) m map.Path
      let m' = m

      let expectedHp(eid: int<EnemyId>) =
        if eid = 1<EnemyId> then Fixtures.tank.Hp - 50
        elif eid = 0<EnemyId> then Fixtures.grunt.Hp
        else Fixtures.runner.Hp

      let views = viewsView m'

      for KeyValueV(eid, v) in views do
        Expect.equal v.Hp (expectedHp eid) $"hp of enemy %d{int eid}"

      // Alive still holds all three (nothing dead).
      Expect.equal (aliveView m').Count 3 "all alive")

    testCase "Alive drops corpses; Views keeps them until despawn" (fun () ->
      let mutable m = spawn (Enemies.init()) Fixtures.grunt
      m <- spawn m Fixtures.runner

      let _ = Enemies.handle (EnemyMsg.ApplyDamage(0<EnemyId>, 999)) m map.Path
      let m' = m

      Expect.equal (aliveView m').Count 1 "only runner alive"
      Expect.equal (viewsView m').Count 2 "corpse still joined"

      Expect.equal
        (m'.Alive |> AMap.count |> AVal.getValue)
        1
        "count follows Alive"

      let _ = Enemies.handle (EnemyMsg.Despawn(0<EnemyId>)) m' map.Path
      let m2 = m'

      Expect.equal (viewsView m2).Count 1 "corpse removed"

      Expect.equal
        (m2.Alive |> AMap.count |> AVal.getValue)
        1
        "count unchanged")

    testCase "repeated reads at a settled state are stable" (fun () ->
      let mutable m = spawn (Enemies.init()) Fixtures.tank

      let _ = Enemies.handle (EnemyMsg.ApplyDamage(0<EnemyId>, 10)) m map.Path
      let m' = m

      let first = viewsView m'
      let second = viewsView m'
      Expect.equal first.Count second.Count "stable count"

      for KeyValueV(eid, v) in first do
        match second |> ReadOnlyDict.tryGetValue eid with
        | ValueSome v2 -> Expect.equal v v2 "stable row"
        | ValueNone -> failtest "row vanished")

    testCase
      "PlacementPreview affordability follows the selected tower"
      (fun () ->
        let economy = Economy.Economy.init cfg // gold 100
        let towers = Towers.Towers.init()
        let projectiles = Projectiles.Projectiles.init()
        let hover = CVal.create(ValueSome(struct (1, 1))) // buildable grass
        let selected = CVal.create TowerDefs.arrowDeck // cost 70

        let projections =
          Projections(
            Enemies.init(),
            towers,
            projectiles,
            economy,
            MapModel.buildableGrid map,
            hover,
            selected
          )

        // Gold 100 ≥ arrow deck 70 → affordable.
        Expect.equal
          (AVal.getValue projections.PlacementPreview)
          PlacementStatus.Affordable
          "affordable at 100"

        // 60: enough for the sentry (40), NOT for the arrow deck
        // (70) — the preview must reflect the SELECTED tower, not
        // the cheapest.
        economy.Gold |> CVal.set 60

        Expect.equal
          (AVal.getValue projections.PlacementPreview)
          PlacementStatus.TooExpensive
          "arrow deck too expensive at 60"

        selected |> CVal.set TowerDefs.sentry

        Expect.equal
          (AVal.getValue projections.PlacementPreview)
          PlacementStatus.Affordable
          "sentry affordable at 60")

    testCase "TowerAim: per tower, the runtime's aim position" (fun () ->
      let enemies = Enemies.init()
      let towers = Towers.Towers.init()

      Towers.Towers.handle
        (Towers.TowerMsg.Place(struct (2, 3), TowerDefs.sentry))
        towers

      // No target yet → no aim.
      let projections =
        Projections(
          enemies,
          towers,
          Projectiles.Projectiles.init(),
          Economy.Economy.init cfg,
          MapModel.buildableGrid map,
          CVal.create ValueNone,
          CVal.create TowerDefs.sentry
        )

      Expect.equal
        (projections.TowerAim
         |> AMap.getValue
         |> ReadOnlyDict.tryGetValue(0<TowerId>))
        (ValueSome ValueNone)
        "idle tower has no aim"

      // A held target's position lands in the projection.
      let aim = Vector2(4.5f, 3.5f)

      towers.Runtimes
      |> CMap.addOrUpdate (0<TowerId>) {
        Cooldown = 0.2f
        Target = ValueSome(0<EnemyId>)
        Aim = ValueSome aim
      }

      Expect.equal
        (projections.TowerAim
         |> AMap.getValue
         |> ReadOnlyDict.tryGetValue(0<TowerId>))
        (ValueSome(ValueSome aim))
        "aim tracks the runtime")

    testCase "game over aval follows lives" (fun () ->
      let e = Economy.init cfg
      Expect.isFalse (AVal.getValue e.GameOver) "not over"
      Economy.handle EconomyMsg.LoseLife e
      Expect.isFalse (AVal.getValue e.GameOver) "still not over"

      for _ in 2 .. cfg.StartingLives do
        Economy.handle EconomyMsg.LoseLife e

      Expect.isTrue (AVal.getValue e.GameOver) "over at zero")

    testCase "Suppression: boss in radius suppresses, others don't" (fun () ->
      let enemies = Enemies.init()
      let towers = Towers.Towers.init()

      // Tower at cell (2,3) — center (2.5, 3.5) in world units.
      Towers.Towers.handle
        (Towers.TowerMsg.Place(struct (2, 3), TowerDefs.sentry))
        towers

      let t' = towers

      let projections =
        Projections(
          enemies,
          t',
          Projectiles.Projectiles.init(),
          Economy.Economy.init cfg,
          MapModel.buildableGrid map,
          CVal.create ValueNone,
          CVal.create TowerDefs.sentry
        )

      let factorOf(tid: int<TowerId>) =
        projections.Suppression
        |> AMap.getValue
        |> ReadOnlyDict.tryGetValue tid

      // No boss in the world → free (factor 1).
      Expect.equal (factorOf(0<TowerId>)) (ValueSome 1f) "free without boss"

      // Boss ON the tower's cell → suppressed (Radius 2 units).
      let _ =
        Enemies.handle
          (EnemyMsg.SpawnAt(Fixtures.boss, Vector2(2.5f, 3.5f), 0f, 0))
          enemies
          map.Path

      let e2 = enemies

      Expect.equal
        (factorOf(0<TowerId>))
        (ValueSome BossAura.Factor)
        "suppressed in radius"

      // A non-boss at the same spot does NOT suppress.
      let _ = Enemies.handle (EnemyMsg.Despawn(0<EnemyId>)) e2 map.Path
      let e3 = e2

      let _ =
        Enemies.handle
          (EnemyMsg.SpawnAt(Fixtures.grunt, Vector2(2.5f, 3.5f), 0f, 0))
          e3
          map.Path

      let e4 = e3

      Expect.equal
        (factorOf(0<TowerId>))
        (ValueSome 1f)
        "grunts don't suppress"

      // A boss OUTSIDE the radius (3 units away > Radius 2) doesn't either.
      let _ = Enemies.handle (EnemyMsg.Despawn(1<EnemyId>)) e4 map.Path
      let e5 = e4

      let _ =
        Enemies.handle
          (EnemyMsg.SpawnAt(Fixtures.boss, Vector2(5.5f, 3.5f), 0f, 0))
          e5
          map.Path

      Expect.equal (factorOf(0<TowerId>)) (ValueSome 1f) "out of radius")
  ]
