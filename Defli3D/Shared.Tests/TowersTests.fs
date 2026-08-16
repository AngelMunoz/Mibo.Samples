module Defli3D.Tests.TowersTests

open System.Collections.Generic
open System.Numerics
open Expecto
open Mibo.Adaptive
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open TestData
open Defli3D.State.Systems.Towers

let private cfg = TestData.Fixtures.cfg
let private map = MapModel.create cfg
let private cellSize = Vector2(1f, 1f) // 1 cell = 1 world unit
let private model() = Towers.init()

/// Test-owned tower def — distinct values catch production mix-ups.
let private def = {
  Key = "test_gun"
  Name = "Test Gun"
  Chassis = Chassis.Emplacement
  Cost = 30
  Range = 2
  Damage = 5
  FireRate = 4f
  RatePerLevel = 0.25f // distinct from the presets' 0.1 — pins the curve source
  ProjectileSpeed = 3.125f // 200 px/s ÷ 64
  ProjectileSpeedScales = false
  Volley = 1
  Spread = 0f
  Trajectory = Trajectory.Flat
  ImpactRadius = 0.25f
  Piercing = false
  Homing = HomingPolicy.FromLevel 4
  Zone = ValueNone
  WeaponModel = ValueSome Models.weaponTurret
  GunScale = 1f
  ProjectileModel = Models.ammoBullet
  ProjectileScale = 0.7f
  MuzzleDust = false
  Targets = TargetDomain.Any
  TargetPolicy = TargetPolicy.First
  UpgradeCost = 20
  MaxLevel = 5
}

/// The fixture def with a specific targeting policy.
let private defWith(policy: TargetPolicy) = { def with TargetPolicy = policy }

/// A single enemy of the given archetype at a position (transient
/// Alive-shaped dict).
let private enemyOf
  (archetype: EnemyArchetype)
  (pos: Vector2)
  (progress: float32)
  =
  let d = Dictionary<int<EnemyId>, EnemyView>()

  d[0<EnemyId>] <- {
    Pos = pos
    Hp = 100
    MaxHp = 100
    Archetype = archetype
    Progress = progress
    Slow = 1f
    PathIndex = 1
  }

  d

let private enemyAt (pos: Vector2) (progress: float32) =
  enemyOf EnemyArchetype.Grunt pos progress

/// A single flier — the air-domain target.
let private flierAt (pos: Vector2) (progress: float32) =
  enemyOf EnemyArchetype.Flier pos progress

let private cellCenter(struct (x, y)) =
  Vector2(
    float32 x * cellSize.X + cellSize.X / 2f,
    float32 y * cellSize.Y + cellSize.Y / 2f
  )

/// No boss aura in scope — an empty suppression map (factor 1 = free).
let private noSuppression = Dictionary<int<TowerId>, float32>()

/// No measured velocities — stationary targets (prediction = pos).
let private noVelocities = Dictionary<int<EnemyId>, Vector2>()

let tests =
  testList "Towers" [
    testCase "place writes Statics + Runtimes + CellIndex atomically" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.handle (TowerMsg.Place(cell, def)) m
      let m' = m

      Expect.equal (m'.Statics |> AMap.getValue).Count 1 "statics"
      Expect.equal (m'.Runtimes |> AMap.getValue).Count 1 "runtimes"

      match m'.CellIndex |> CMap.tryGetValue cell with
      | ValueSome tid -> Expect.equal tid (0<TowerId>) "indexed"
      | ValueNone -> failtest "cell must be indexed"

      match m'.Statics |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome s -> Expect.equal s.Def def "def stored"
      | ValueNone -> failtest "tower must exist")

    testCase "no target in range → no fire, cooldown stays ready" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.handle (TowerMsg.Place(cell, def)) m
      let m' = m

      // Range 2 cells = 2 units; enemy is far away.
      let alive = AMap.constant(fun () -> enemyAt (Vector2(900f, 900f)) 0.5f)

      let events =
        Towers.tick 0.1f m' alive noVelocities noSuppression cellSize

      let m2 = m'

      Expect.isEmpty events "no fire"
      Expect.equal (Seq.length events) 0 "no events"

      match m2.Runtimes |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome r -> Expect.equal r.Cooldown 0f "ready"
      | ValueNone -> failtest "runtime must exist")

    testCase "enemy in range → Fired with damage; cooldown set" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.handle (TowerMsg.Place(cell, def)) m
      let m' = m

      // Tower center (3,3) = (3.5, 3.5); enemy one cell east = in range 2.
      let alive =
        AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

      let events =
        Towers.tick 0.1f m' alive noVelocities noSuppression cellSize

      let m2 = m'

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.Tower (0<TowerId>) "tower id"
        Expect.equal shot.Enemy (ValueSome(0<EnemyId>)) "enemy id"
        Expect.equal shot.Damage def.Damage "damage"
      | _ -> failtest "expected exactly one Fired"

      match m2.Runtimes |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome r ->
        Expect.equal r.Cooldown (1f / def.FireRate) "cooldown set"
        Expect.equal r.Target (ValueSome(0<EnemyId>)) "target stored"
      | ValueNone -> failtest "runtime must exist")

    testCase "first policy: picks the enemy closest to the base" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.handle (TowerMsg.Place(cell, def)) m
      let m' = m

      // Two enemies both in range; the one with higher progress wins.
      let alive = Dictionary<int<EnemyId>, EnemyView>()

      alive[1<EnemyId>] <- {
        Pos = cellCenter struct (4, 3)
        Hp = 100
        MaxHp = 100
        Progress = 0.2f
        Slow = 1f
        PathIndex = 1
        Archetype = EnemyArchetype.Grunt
      }

      alive[2<EnemyId>] <- {
        Pos = cellCenter struct (4, 2)
        Hp = 100
        MaxHp = 100
        Progress = 0.8f
        Slow = 1f
        PathIndex = 1
        Archetype = EnemyArchetype.Grunt
      }

      let alive = AMap.constant(fun () -> alive)

      let events =
        Towers.tick 0.1f m' alive noVelocities noSuppression cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal
          shot.Enemy
          (ValueSome(2<EnemyId>))
          "first = highest progress"
      | _ -> failtest "expected exactly one Fired")

    // ── Phase 3: targeting policies ──

    /// Two in-range enemies: id 1 = progress 0.2, 100 hp / 100 max;
    /// id 2 = progress 0.8, 60 hp / 40 max (both at (4,3)).
    let twoInRange =
      let alive = Dictionary<int<EnemyId>, EnemyView>()

      alive[1<EnemyId>] <- {
        Pos = cellCenter struct (4, 3)
        Hp = 100
        MaxHp = 100
        Progress = 0.2f
        Slow = 1f
        PathIndex = 1
        Archetype = EnemyArchetype.Grunt
      }

      alive[2<EnemyId>] <- {
        Pos = cellCenter struct (4, 3)
        Hp = 60
        MaxHp = 40
        Progress = 0.8f
        Slow = 1f
        PathIndex = 1
        Archetype = EnemyArchetype.Grunt
      }

      alive

    /// The enemy id the policy picks from `twoInRange`.
    let picked(policy: TargetPolicy) : int<EnemyId> voption =
      let m = model()

      Towers.handle (TowerMsg.Place(struct (3, 3), defWith policy)) m
      let m' = m

      let events =
        Towers.tick
          0.1f
          m'
          (AMap.constant(fun () -> twoInRange))
          noVelocities
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] -> shot.Enemy
      | _ -> failtest "expected exactly one Fired"

    testCase "policy Last: lowest progress wins" (fun () ->
      Expect.equal (picked TargetPolicy.Last) (ValueSome(1<EnemyId>)) "last")

    testCase "policy Strongest: highest max HP wins" (fun () ->
      Expect.equal
        (picked TargetPolicy.Strongest)
        (ValueSome(1<EnemyId>))
        "strongest")

    testCase "policy Weakest: lowest current HP wins" (fun () ->
      Expect.equal
        (picked TargetPolicy.Weakest)
        (ValueSome(2<EnemyId>))
        "weakest")

    testCase "policy Closest: nearest enemy wins" (fun () ->
      let alive = Dictionary<int<EnemyId>, EnemyView>()

      alive[1<EnemyId>] <- {
        Pos = cellCenter struct (3, 3) + Vector2(0.625f, 0f) // 40 px ÷ 64
        Hp = 100
        MaxHp = 100
        Progress = 0.5f
        Slow = 1f
        PathIndex = 1
        Archetype = EnemyArchetype.Grunt
      }

      alive[2<EnemyId>] <- {
        Pos = cellCenter struct (3, 3) + Vector2(1.5625f, 0f) // 100 px ÷ 64
        Hp = 100
        MaxHp = 100
        Progress = 0.5f
        Slow = 1f
        PathIndex = 1
        Archetype = EnemyArchetype.Grunt
      }

      let m = model()

      Towers.handle
        (TowerMsg.Place(struct (3, 3), defWith TargetPolicy.Closest))
        m

      let m' = m

      let events =
        Towers.tick
          0.1f
          m'
          (AMap.constant(fun () -> alive))
          noVelocities
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.Enemy (ValueSome(1<EnemyId>)) "closest"
      | _ -> failtest "expected exactly one Fired")

    // ── Ballistic rework: prediction / volley / seek / aim ──

    testCase
      "lead prediction: Aim leads a moving target by vel × flight"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.handle (TowerMsg.Place(cell, def)) m
        let m' = m

        let enemyPos = cellCenter struct (4, 3) // (4.5, 3.5) — 1 unit east
        let alive = AMap.constant(fun () -> enemyAt enemyPos 0.5f)

        // The target marches +X at 0.5 units/s (the movement tick's
        // measured velocity).
        let velocities = Dictionary<int<EnemyId>, Vector2>()
        velocities[0<EnemyId>] <- Vector2(0.5f, 0f)

        let events =
          Towers.tick 0.1f m' alive velocities noSuppression cellSize

        let flight =
          Vector2.Distance(cellCenter cell, enemyPos) / def.ProjectileSpeed

        let expected = enemyPos + Vector2(0.5f, 0f) * flight

        match events |> Seq.toArray with
        | [| Fired shot |] ->
          Expect.equal shot.Aim expected "aim leads the target"
        | _ -> failtest "expected exactly one Fired")

    testCase "stationary target: Aim equals the target position" (fun () ->
      let m = model()

      Towers.handle (TowerMsg.Place(struct (3, 3), def)) m

      let enemyPos = cellCenter struct (4, 3)
      let alive = AMap.constant(fun () -> enemyAt enemyPos 0.5f)

      let events = Towers.tick 0.1f m alive noVelocities noSuppression cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] -> Expect.equal shot.Aim enemyPos "no lead"
      | _ -> failtest "expected exactly one Fired")

    testCase
      "Fired.Muzzle is offset along the firing line, not the center"
      (fun () ->
        let m = model()

        Towers.handle (TowerMsg.Place(struct (3, 3), def)) m

        let alive =
          AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

        let events =
          Towers.tick 0.1f m alive noVelocities noSuppression cellSize

        // The turret's barrel half-length, scaled: shots leave the gun,
        // not the tower's middle.
        let expectedReach =
          def.WeaponModel.Value.SizeZ
          * 0.5f
          * def.GunScale
          * TowerLayout.towerScale

        match events |> Seq.toArray with
        | [| Fired shot |] ->
          let expected = cellCenter struct (3, 3) + Vector2(expectedReach, 0f)

          Expect.isTrue (abs(shot.Muzzle.X - expected.X) < 0.0001f) "muzzle x"

          Expect.isTrue (abs(shot.Muzzle.Y - expected.Y) < 0.0001f) "muzzle y"
        | _ -> failtest "expected exactly one Fired")

    testCase "seek follows the def's HomingPolicy" (fun () ->
      let seekAt (d: TowerDef) (level: int) =
        let m = model()

        Towers.handle (TowerMsg.Place(struct (3, 3), d)) m

        for _ in 2..level do
          Towers.handle (TowerMsg.Upgrade(0<TowerId>)) m

        let events =
          Towers.tick
            0.1f
            m
            (AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f))
            noVelocities
            noSuppression
            cellSize

        match events |> Seq.toArray with
        | [| Fired shot |] -> shot.Seek
        | _ -> failtest "expected exactly one Fired"

      // FromLevel 4 (guns): dumbfire below, seeking from level 4.
      Expect.isFalse (seekAt def 1) "level 1 is dumbfire"
      Expect.isFalse (seekAt def 3) "level 3 is dumbfire"
      Expect.isTrue (seekAt def 4) "level 4 seeks"
      Expect.isTrue (seekAt def 5) "level 5 seeks"
      // Never (loaders): dumbfire at EVERY level.
      Expect.isFalse
        (seekAt { def with Homing = HomingPolicy.Never } 5)
        "loaders never seek"
      // Always (rockets): seeking from level 1.
      Expect.isTrue
        (seekAt
          {
            def with
                Homing = HomingPolicy.Always
          }
          1)
        "rockets always seek")

    // Preset homing/rate VALUES are tuning data (playtest-owned) —
    // not pinned here. What is pinned is the mechanism: the policy
    // gates ("seek follows the def's HomingPolicy", above) and the
    // upgrade formula this suite applies to every preset:
    testCase
      "effectiveDef: the upgrade formula holds for every preset"
      (fun () ->
        for d in TowerDefs.all do
          // Level 1 IS the base def — upgrades never change the start.
          Expect.equal
            (TowerDefs.effectiveDef d 1)
            d
            $"{d.Key}: level 1 = base def"

          let mutable prevRate = d.FireRate

          for level = 2 to d.MaxLevel do
            let eff = TowerDefs.effectiveDef d level
            let l = float32(level - 1)

            // The formula, read from the def itself — no pinned
            // numbers: FireRate = base·(1 + RatePerLevel·l),
            // Damage = +25 %/level, Range = +0.5/level.
            Expect.isTrue
              (abs(
                float eff.FireRate
                - float d.FireRate * (1.0 + float d.RatePerLevel * float l)
              ) < 0.0001)
              $"{d.Key}: fire rate formula at L%d{level}"

            Expect.equal
              eff.Damage
              (int(float d.Damage * (1.0 + 0.25 * float l)))
              $"{d.Key}: damage formula at L%d{level}"

            Expect.equal
              eff.Range
              (d.Range + int(l * 0.5f))
              $"{d.Key}: range formula at L%d{level}"

            // Monotone: every level fires at least as fast.
            Expect.isGreaterThanOrEqual
              eff.FireRate
              prevRate
              $"{d.Key}: rate monotone at L%d{level}"

            prevRate <- eff.FireRate

      )

    testCase "Ground weapons ignore fliers; Any weapons engage them" (fun () ->
      let m = model()

      Towers.handle
        (TowerMsg.Place(
          struct (3, 3),
          {
            def with
                Targets = TargetDomain.Ground
          }
        ))
        m

      // Only a flier in range: the cannon idles.
      let events =
        Towers.tick
          0.1f
          m
          (AMap.constant(fun () -> flierAt (cellCenter struct (4, 3)) 0.5f))
          noVelocities
          noSuppression
          cellSize

      Expect.isEmpty events "no shot at the flier"

      // A walker appears: fires.
      let events2 =
        Towers.tick
          0.1f
          m
          (AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f))
          noVelocities
          noSuppression
          cellSize

      Expect.equal (Seq.length events2) 1 "fires at the walker"

      // The Any-domain def (the local test def) engages the flier.
      let m2 = model()
      Towers.handle (TowerMsg.Place(struct (3, 3), def)) m2

      let events3 =
        Towers.tick
          0.1f
          m2
          (AMap.constant(fun () -> flierAt (cellCenter struct (4, 3)) 0.5f))
          noVelocities
          noSuppression
          cellSize

      Expect.equal (Seq.length events3) 1 "arrow/bullet weapons target fliers")

    testCase "preset Targets follow the trajectory rule (Flat = Any)" (fun () ->
      // A cross-field design rule, not pinned values: every preset
      // that fires flat can engage fliers; lobbed weapons are
      // ground-only. (Zone Affects behavior is covered functionally
      // in ZonesTests: "Ground zones skip fliers" / "Any zones tick
      // fliers" — the per-preset zone domains themselves are tuning
      // data.)
      for d in TowerDefs.all do
        let expected =
          if d.Trajectory = Trajectory.Flat then
            TargetDomain.Any
          else
            TargetDomain.Ground

        Expect.equal d.Targets expected $"%s{d.Key} targets by trajectory")

    testCase "volley def → Fired carries the volley payload" (fun () ->
      let m = model()
      let d = { def with Volley = 4; Spread = 0.6f }

      Towers.handle (TowerMsg.Place(struct (3, 3), d)) m

      let events =
        Towers.tick
          0.1f
          m
          (AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f))
          noVelocities
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.Volley 4 "volley count"
        Expect.equal shot.Spread 0.6f "spread"
      | _ -> failtest "expected exactly one Fired")

    testCase "zone weapon (bunker) → Fired carries the zone payload" (fun () ->
      let m = model()

      Towers.handle (TowerMsg.Place(struct (3, 3), TowerDefs.bunker)) m

      let events =
        Towers.tick
          0.1f
          m
          (AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f))
          noVelocities
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.Zone TowerDefs.bunker.Zone "zone payload"

        Expect.equal
          shot.ProjectileModel
          TowerDefs.bunker.ProjectileModel
          "shell model"
      | _ -> failtest "expected exactly one Fired")

    testCase "piercer def → Fired carries piercing" (fun () ->
      let m = model()

      Towers.handle (TowerMsg.Place(struct (3, 3), TowerDefs.piercer)) m

      let events =
        Towers.tick
          0.1f
          m
          (AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f))
          noVelocities
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.isTrue shot.Piercing "piercing shot"
        Expect.isFalse shot.Seek "piercer never seeks (Never policy)"
      | _ -> failtest "expected exactly one Fired")

    testCase
      "Runtimes.Aim tracks the acquired target (TowerAim feed)"
      (fun () ->
        let m = model()

        Towers.handle (TowerMsg.Place(struct (3, 3), def)) m

        let enemyPos = cellCenter struct (4, 3)

        Towers.tick
          0.1f
          m
          (AMap.constant(fun () -> enemyAt enemyPos 0.5f))
          noVelocities
          noSuppression
          cellSize
        |> ignore

        match m.Runtimes |> CMap.tryGetValue(0<TowerId>) with
        | ValueSome r -> Expect.equal r.Aim (ValueSome enemyPos) "aim stored"
        | ValueNone -> failtest "runtime must exist")

    testCase
      "cooldown comes from the EFFECTIVE def (upgraded fire rate)"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.handle (TowerMsg.Place(cell, def)) m
        let m' = m

        // Level 2: +RatePerLevel fire rate → the cooldown must shrink.
        Towers.handle (TowerMsg.Upgrade(0<TowerId>)) m'
        let m2 = m'

        let alive =
          AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

        let events =
          Towers.tick 0.1f m2 alive noVelocities noSuppression cellSize

        let m3 = m2

        Expect.equal (Seq.length events) 1 "fired"

        match m3.Runtimes |> CMap.tryGetValue(0<TowerId>) with
        | ValueSome r ->
          Expect.equal
            r.Cooldown
            (1f / (def.FireRate * (1f + def.RatePerLevel)))
            "cooldown uses the upgraded fire rate"
        | ValueNone -> failtest "runtime must exist")

    testCase
      "boss aura suppression halves the fire rate (double cooldown)"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.handle (TowerMsg.Place(cell, def)) m
        let m' = m

        let suppression = Dictionary<int<TowerId>, float32>()
        suppression[0<TowerId>] <- BossAura.Factor

        let alive =
          AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

        let events =
          Towers.tick 0.1f m' alive noVelocities suppression cellSize

        let m2 = m'

        Expect.equal (Seq.length events) 1 "fired"

        match m2.Runtimes |> CMap.tryGetValue(0<TowerId>) with
        | ValueSome r ->
          Expect.equal
            r.Cooldown
            (1f / (def.FireRate * BossAura.Factor))
            "suppressed cooldown"
        | ValueNone -> failtest "runtime must exist")

    testCase
      "Upgrade bumps the level; EffectiveDef composes scaled stats"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.handle (TowerMsg.Place(cell, def)) m
        let m' = m
        let tid = 0<TowerId>

        // Level 1 → the base def.
        let eff1 =
          m'.EffectiveDef |> AMap.getValue |> ReadOnlyDict.tryGetValue tid

        match eff1 with
        | ValueSome e -> Expect.equal e.Damage def.Damage "base damage"
        | ValueNone -> failtest "effective def must exist"

        // Level 2 → +25 % damage, +RatePerLevel fire rate, +0.5 range.
        Towers.handle (TowerMsg.Upgrade tid) m'
        let m2 = m'

        match m2.Levels |> CMap.tryGetValue tid with
        | ValueSome lvl -> Expect.equal lvl 2 "level stored"
        | ValueNone -> failtest "level must exist"

        let eff2 =
          m2.EffectiveDef |> AMap.getValue |> ReadOnlyDict.tryGetValue tid

        match eff2 with
        | ValueSome e ->
          Expect.equal e.Damage (int(float def.Damage * 1.25)) "scaled damage"
          Expect.equal e.Range (def.Range + 0) "range round-half-down"
        | ValueNone -> failtest "effective def must exist")

    testCase "cooldown gates firing" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.handle (TowerMsg.Place(cell, def)) m
      let m' = m

      let alive =
        AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

      // Fire (cooldown = 0.25 at FireRate 4).
      let events =
        Towers.tick 0.1f m' alive noVelocities noSuppression cellSize

      let m2 = m'

      Expect.equal (Seq.length events) 1 "fired once"

      // 0.1 s later: still cooling down (0.25 - 0.1 = 0.15).
      let events2 =
        Towers.tick 0.1f m2 alive noVelocities noSuppression cellSize

      let m3 = m2

      Expect.isEmpty events2 "not ready yet"

      // 0.2 s more: 0.15 - 0.2 ≤ 0 → fires again.
      let events3 =
        Towers.tick 0.2f m3 alive noVelocities noSuppression cellSize

      Expect.equal (Seq.length events3) 1 "fired again")
  ]
