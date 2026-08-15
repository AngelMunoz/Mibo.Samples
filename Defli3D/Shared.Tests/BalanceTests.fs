module Defli3D.Tests.BalanceTests

open System.Numerics
open Expecto
open Mibo.Adaptive
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Towers

// ─────────────────────────────────────────────────────────────
// The margin harness — the tuning instrument for Balance.fs.
// It prints the difficulty table (per tier × player profile) so a
// knob change can be READ before it is felt, and pins the design
// invariants: perfect play never drops below margin 1 through the
// calibration horizon, the calibration anchor reads exactly Alpha,
// and imperfect play dips under 1 in the mid band (the challenge).
//
//   M(t) = η · PowerTotal · (1 − ρ(t)) / (v(t) · avgHp · s(t))
//
// (η = the player's fraction of the greedy capacity fill — enemy
// count cancels: towers fire continuously at the stream.)
// ─────────────────────────────────────────────────────────────

let private productionMap = MapModel.create WorldConfig.defaults

let private capacity = Balance.capacityOf productionMap

let private sat = capacity.Saturation

/// The tier's margin for a player holding η of the map's power.
let private margin (eta: float32) (tier: int) : float32 =
  let scale = Balance.scaleOfWave sat (tier * 5)

  eta * capacity.PowerTotal * (1f - scale.Resist)
  / (scale.Speed * Balance.avgHpNormal * scale.Hp)

/// A minimal synthetic shot at a tower — drives the fired→spawn
/// translation without the targeting tick.
let private shotFor(tid: int<TowerId>) : TowerEvent =
  Fired {
    Tower = tid
    Enemy = ValueNone
    Aim = Vector2(2f, 4.5f)
    Muzzle = Vector2(1.5f, 1.5f)
    Damage = 1
    ImpactRadius = 0.25f
    Piercing = false
    Seek = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
    Zone = ValueNone
    ProjectileModel = Models.ammoBullet
    ProjectileScale = 1f
    Height = 0.5f
    MuzzleDust = false
  }

let tests =
  testList "Balance" [
    // ── The margin table (printed — read it when tuning) ──────
    testCase "margin table: the difficulty envelope" (fun () ->
      printfn
        "\n═══ balance margin table (sat %.1f, power %.0f, buildable %d, road %d) ═══"
        capacity.Saturation
        capacity.PowerTotal
        capacity.Buildable
        capacity.RoadCells

      printfn "  tier  wave     s(t)    rho    eta=0.6  eta=0.8  eta=1.0"

      for t = 0 to 12 do
        let scale = Balance.scaleOfWave sat (t * 5)

        printfn
          "  %4d  %4d  %7.2f  %.3f  %7.2f  %7.2f  %7.2f"
          t
          (t * 5)
          scale.Hp
          scale.Resist
          (margin 0.6f t)
          (margin 0.8f t)
          (margin 1.0f t)

      // Perfect play stays above margin 1 through the calibration
      // horizon — the "never definitively impossible" guarantee.
      for t = 0 to int Balance.RefTier do
        Expect.isGreaterThanOrEqual
          (margin 1.0f t)
          1.0f
          $"perfect play holds at tier %d{t}"

      // The calibration anchor: the horizon build's margin reads
      // exactly Alpha at RefTier — the fixed-point sweep settled.
      Expect.isTrue
        (abs(margin Balance.HorizonBuild (int Balance.RefTier) - Balance.Alpha) < 0.02f)
        "anchor reads Alpha at RefTier"

      // The uncapped-speed squeeze is REAL and visible: past the
      // inflection the margin erodes every tier (enemy count cancels;
      // speed is the only asymptotic erosion — by design).
      for t = 4 to 11 do
        Expect.isLessThan
          (margin 1.0f (t + 1))
          (margin 1.0f t)
          $"margin erodes at tier %d{t}"

      Expect.isLessThan
        (margin 0.6f 12)
        (0.8f * margin 0.6f 6)
        "the squeeze compounds across tiers"

      // The early ramp stays survivable for weak fills.
      Expect.isGreaterThanOrEqual (margin 0.6f 0) 1.2f "early ramp")

    // ── The economy table (printed — read it when tuning) ──────
    testCase "economy table: income tracks the equipment bill" (fun () ->
      // Mirrors the sim: the per-kill max 1 gold floor
      // (WaveScale.apply), the 4/2/1/1 representative mix, one
      // boss per tier, clears via Balance.clearBonus (base 25).
      // Splits and per-modulo mix variance are approximated away —
      // the table is for reading, the band assertions are the
      // contract.
      printfn
        "\n═══ economy table (clearShare %.2f, killShare %.2f, gold/power %.2f) ═══"
        Balance.ClearShare
        Balance.KillShare
        Balance.GoldPerPower

      printfn "  tier  wave     g(t)    kills  clears   cum    bill  ratio"

      let mutable cum = 0f

      let mutable ratio = 0f

      for t = 0 to 12 do
        let scale = Balance.scaleOfWave sat (5 * t)

        let floored(d: int) =
          max 1 (int(float d * float scale.Reward))

        let mix =
          (floored EnemyDefs.grunt.GoldReward * 4
           + floored EnemyDefs.runner.GoldReward * 2
           + floored EnemyDefs.tank.GoldReward
           + floored EnemyDefs.flier.GoldReward)

        let count = if t = 0 then 40 else 45 + 50 * t

        let kills =
          float32 count / 8f * float32 mix
          + (if t >= 1 then
               float32(floored EnemyDefs.boss.GoldReward)
             else
               0f)

        let clears =
          float32((if t = 0 then 4 else 5) * Balance.clearBonus 25 sat (5 * t))

        cum <- cum + kills + clears

        let billNow = Balance.bill sat t
        ratio <- cum / billNow

        printfn
          "  %4d  %4d  %7.3f  %6.0f  %6.0f  %6.0f  %6.0f  %5.2f"
          t
          (t * 5)
          scale.Reward
          kills
          clears
          cum
          billNow
          ratio

      // Never floods, never starves: cumulative income stays in a
      // band around the bill (the early float — starting gold's
      // tier-0 head start — keeps low tiers above 1).
      for t = 3 to 8 do
        let cumAt =
          [ 0..t ]
          |> List.sumBy(fun tt ->
            let scale = Balance.scaleOfWave sat (5 * tt)

            let floored(d: int) =
              max 1 (int(float d * float scale.Reward))

            let mix =
              (floored EnemyDefs.grunt.GoldReward * 4
               + floored EnemyDefs.runner.GoldReward * 2
               + floored EnemyDefs.tank.GoldReward
               + floored EnemyDefs.flier.GoldReward)

            let count = if tt = 0 then 40 else 45 + 50 * tt

            let kills =
              float32 count / 8f * float32 mix
              + (if tt >= 1 then
                   float32(floored EnemyDefs.boss.GoldReward)
                 else
                   0f)

            let clears =
              float32(
                (if tt = 0 then 4 else 5) * Balance.clearBonus 25 sat (5 * tt)
              )

            kills + clears)

        let r = cumAt / Balance.bill sat t

        Expect.isGreaterThan
          r
          0.7f
          $"cumulative income above 0.7×bill at %d{t}"

        Expect.isLessThan r 1.4f $"cumulative income below 1.4×bill at %d{t}")

    // ── Curve structure ───────────────────────────────────────
    testCase "logistic curves: exact base, monotone, bounded" (fun () ->
      let s0 = Balance.scaleOfWave sat 1
      Expect.equal s0.Hp 1f "tier 0 is exactly the base defs"
      Expect.equal s0.Speed 1f "tier 0 speed unscaled"
      Expect.equal s0.Reward 1f "tier 0 reward unscaled"
      Expect.equal s0.Resist 0f "tier 0 has no resistance"

      let mutable prev = 1f

      for t = 1 to 20 do
        let s = Balance.scaleOfWave sat (t * 5)
        // Non-decreasing: past ~tier 17 the float32 logistic is
        // fully saturated (equal on consecutive tiers).
        Expect.isGreaterThanOrEqual s.Hp prev $"hp grows at tier %d{t}"

        // (≤ not <: past ~tier 13 the logistic saturates to the
        // saturation's float32 value exactly.)
        Expect.isLessThanOrEqual s.Hp sat $"hp bounded by saturation at %d{t}"

        Expect.isLessThanOrEqual
          s.Resist
          Balance.RhoMax
          $"resist capped at %d{t}"

        // Reward: bill-anchored, positive, and never a flood —
        // the failed s^0.8 coupling paid up to 13.5× base.
        Expect.isGreaterThan s.Reward 0f $"reward positive at %d{t}"

        Expect.isLessThan s.Reward 2f $"reward bounded at %d{t}"

        // Speed compounds uncapped.
        Expect.isTrue
          (abs(s.Speed - Balance.SpeedGrowth ** float32 t) < 0.0001f)
          $"speed = growth^t at %d{t}"

        prev <- s.Hp

      // Resistance is ~absent early (the tuned wave-1 start keeps
      // its two-tower difficulty) and near the cap at the horizon.
      Expect.isLessThan
        (Balance.scaleOfWave sat 5).Resist
        0.1f
        "early resist ~0"

      Expect.isGreaterThan
        (Balance.scaleOfWave sat 30).Resist
        (Balance.RhoMax * 0.9f)
        "late resist approaches the cap")

    testCase "capacity scan: map-agnostic invariants" (fun () ->
      Expect.isGreaterThan capacity.Buildable 0 "buildable cells exist"
      Expect.isGreaterThan capacity.RoadCells 0 "road cells exist"
      Expect.isGreaterThan capacity.Saturation 1f "saturation above base"

      // Deterministic per map: the same map scans to the same
      // capacity (a map rework changes the value, never the
      // contract).
      let again = Balance.capacityOf productionMap

      Expect.equal again.Buildable capacity.Buildable "buildable stable"
      Expect.equal again.Saturation capacity.Saturation "saturation stable"

      // A different map (the hand-authored fixture variant) scans
      // to a valid, positive capacity of its own.
      let fixture = Balance.capacityOf(MapModel.create TestData.Fixtures.cfg)

      Expect.isGreaterThan fixture.Buildable 0 "fixture buildable cells"
      Expect.isGreaterThan fixture.Saturation 1f "fixture saturation")

    // ── Resistance application (the damage chokepoint) ────────
    testCase "applyDamage: multiplicative resist with floor 1" (fun () ->
      let m = Enemies.Enemies.init()

      Enemies.Enemies.spawn
        {
          TestData.Fixtures.grunt with
              Resist = 0.5f
        }
        m
        productionMap.Path

      match m.Defs |> AMap.getValue |> Seq.tryHead with
      | Some(KeyValueV(eid, _)) ->
        let events = Enemies.Enemies.applyDamage eid 10 m
        Expect.isEmpty events "10 dmg − 50 % = 5 < 30 hp, no kill"

        (match m.Healths |> CMap.tryGetValue eid with
         | ValueSome h -> Expect.equal h.Hp (30 - 5) "resisted to 5"
         | ValueNone -> failtest "health row must exist")

        // Floor: 1 dmg against 50 % resist still deals 1.
        Enemies.Enemies.applyDamage eid 1 m |> ignore

        (match m.Healths |> CMap.tryGetValue eid with
         | ValueSome h -> Expect.equal h.Hp (30 - 5 - 1) "floored to 1"
         | ValueNone -> failtest "health row must exist")
      | None -> failtest "enemy must exist")

    testCase "WaveScale.apply carries the tier resist onto defs" (fun () ->
      let scale = Balance.scaleOfWave sat 10
      let applied = WaveScale.apply scale EnemyDefs.grunt

      Expect.equal applied.Resist scale.Resist "resist carried"
      Expect.isGreaterThan applied.Resist 0f "tier 2 has resistance"
      Expect.equal EnemyDefs.grunt.Resist 0f "base defs stay clean")

    // ── Bullet-speed tracking (the guns-keep-pace rule) ───────
    testCase "bullet towers track wave speed; loaders do not" (fun () ->
      Expect.isTrue TowerDefs.gunpost.ProjectileSpeedScales "gunpost tracks"

      Expect.isTrue
        TowerDefs.bulletDeck.ProjectileSpeedScales
        "bulletdeck tracks"

      let loaders = [|
        TowerDefs.sentry
        TowerDefs.piercer
        TowerDefs.arrowDeck
        TowerDefs.cannonPost
        TowerDefs.bunker
        TowerDefs.catapultPost
        TowerDefs.catapult
      |]

      for d in loaders do
        Expect.isFalse d.ProjectileSpeedScales $"%s{d.Key} keeps raw speed"

      // Rockets seek — no accuracy problem to compensate.
      Expect.isFalse
        TowerDefs.rocketPad.ProjectileSpeedScales
        "rocket needs no tracking")

    testCase "fired shots: bullet speed × wave factor at spawn" (fun () ->
      let state = State.init TestData.Fixtures.cfg
      state.Waves.WaveNumber.Set 10 // tier 2

      Towers.Towers.handle
        (TowerMsg.Place(struct (1, 1), TowerDefs.gunpost))
        state.Towers

      Towers.Towers.handle
        (TowerMsg.Place(struct (1, 3), TowerDefs.sentry))
        state.Towers

      Application.handleTowerEvents state [|
        shotFor 0<TowerId>
        shotFor 1<TowerId>
      |]

      let rows = state.Projectiles.Rows |> AMap.getValue

      Expect.hasLength rows 2 "two projectiles spawned"

      for KeyValueV(_, row) in rows do
        // Tier 2 speed factor: 1.07² — the gunpost's 8 × 1.1449;
        // the sentry's 7 stays raw.
        let waveSpeed =
          (Balance.scaleOfWave state.Capacity.Saturation 10).Speed

        let expected =
          if row.Speed > 7.5f then
            TowerDefs.gunpost.ProjectileSpeed * waveSpeed
          else
            TowerDefs.sentry.ProjectileSpeed

        Expect.isTrue
          (abs(row.Speed - expected) < 0.001f)
          $"spawn speed tracks the wave factor (got %f{row.Speed})")
  ]
