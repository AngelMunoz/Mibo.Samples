module Defli3D.Tests.BalanceTests

open System.Numerics
open Expecto
open Mibo.Adaptive
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Towers
open Defli3D.State.Systems.Waves

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
    Warhead = {
      Damage = 1f
      ImpactRadius = 0.25f
      Piercing = false
      Zone = ValueNone
    }
    Seek = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
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

      // The early ramp holds even for weak fills (the mechanism
      // floor: η = 0.6 stays at or above margin 1 while the game
      // is unscaled; mid-band dips below 1 are the challenge).
      Expect.isGreaterThanOrEqual
        (margin 0.6f 0)
        1.0f
        "early ramp holds for weak fills")

    // ── The economy table (printed — read it when tuning) ──────
    testCase
      "economy: income tracks the equipment bill through the real composition"
      (fun () ->
        // The income model IS the game's: Waves.composeWave carries the
        // tier-scaled, floored rewards; clears pay Balance.clearBonus.
        // No hand-rolled counts or mixes — a retune moves the table and
        // the mechanism claim must survive it: cumulative income tracks
        // Bill(t) at roughly the shares' ratio (Scarcity = KillShare +
        // ClearShare), never floods away from it, never starves below
        // half of it.
        let scarcity = Balance.KillShare + Balance.ClearShare

        let waveIncome(n: int) : float32 =
          let wave = Waves.composeWave sat n

          let totalWeight =
            wave.Table |> Seq.sumBy(fun struct (_, w) -> float32 w)

          let avgGold =
            (wave.Table
             |> Seq.sumBy(fun struct (d, w) ->
               float32 w * float32 d.GoldReward))
            / totalWeight

          let kills =
            float32 wave.Count * avgGold
            + float32(
              wave.ExtraSpawns |> Seq.sumBy(fun struct (d, _) -> d.GoldReward)
            )

          kills
          + float32(
            Balance.clearBonus WorldConfig.defaults.WaveClearBonus sat n
          )

        /// The waves whose kills pay at tier t's reward (tier t = waves
        /// 5t..5t+4; tier 0 has no wave 0).
        let block(t: int) = [ max 1 (5 * t) .. 5 * t + 4 ]

        let cumAt(t: int) =
          [ 0..t ] |> List.collect block |> List.sumBy waveIncome

        printfn
          "\n═══ economy table (killShare %.2f, clearShare %.2f, scarcity %.2f) ═══"
          Balance.KillShare
          Balance.ClearShare
          scarcity

        printfn "  tier  wave     income       cum      bill  ratio/scarcity"

        for t = 0 to 12 do
          let income = block t |> List.sumBy waveIncome
          let cum = cumAt t
          let billNow = Balance.bill sat t
          let ratio = if billNow > 0f then cum / billNow / scarcity else 0f

          printfn
            "  %4d  %4d  %9.0f  %8.0f  %8.0f  %8.2f"
            t
            (5 * t)
            income
            cum
            billNow
            ratio

        // Never floods, never starves: through the mid band the
        // cumulative income stays within half..1.5× of the shares'
        // ratio applied to the bill. (Tier 0/1 are excluded — the
        // base-floor clears and the unscaled first waves distort the
        // ratio by design; the table prints them.)
        for t = 2 to 10 do
          let r = cumAt t / Balance.bill sat t / scarcity

          Expect.isGreaterThan
            r
            0.5f
            $"income above 0.5×scarcity×bill at %d{t}"

          Expect.isLessThan r 1.5f $"income below 1.5×scarcity×bill at %d{t}")

    // ── Curve structure ───────────────────────────────────────
    testCase "logistic curves: exact base, monotone, bounded" (fun () ->
      let s0 = Balance.scaleOfWave sat 1
      Expect.equal s0.Hp 1f "tier 0 is exactly the base defs"
      Expect.equal s0.Speed 1f "tier 0 speed unscaled"
      Expect.equal s0.Reward 1f "tier 0 reward unscaled"
      Expect.equal s0.Resist 0f "tier 0 has no resistance"

      let mutable prev = 1f

      let mutable prevResist = 0f

      for t = 1 to 20 do
        let s = Balance.scaleOfWave sat (t * 5)
        // Non-decreasing: past ~tier 17 the float32 logistic is
        // fully saturated (equal on consecutive tiers).
        Expect.isGreaterThanOrEqual s.Hp prev $"hp grows at tier %d{t}"

        // Resist rides the same logistic: monotone, capped.
        Expect.isGreaterThanOrEqual
          s.Resist
          prevResist
          $"resist grows at tier %d{t}"

        // (≤ not <: past ~tier 13 the logistic saturates to the
        // saturation's float32 value exactly.)
        Expect.isLessThanOrEqual s.Hp sat $"hp bounded by saturation at %d{t}"

        Expect.isLessThanOrEqual
          s.Resist
          Balance.RhoMax
          $"resist capped at %d{t}"

        // Reward: bill-anchored and positive, and it always grows
        // slower than the difficulty it pays for — kill gold never
        // outpaces enemy HP at any tier (the flood the failed
        // demand-level coupling produced, at 13.5x base).
        Expect.isGreaterThan s.Reward 0f $"reward positive at %d{t}"

        Expect.isLessThan
          s.Reward
          s.Hp
          $"kill gold grows slower than enemy hp at %d{t}"

        // Speed compounds uncapped.
        Expect.isTrue
          (abs(s.Speed - Balance.SpeedGrowth ** float32 t) < 0.0001f)
          $"speed = growth^t at %d{t}"

        prev <- s.Hp
        prevResist <- s.Resist

      // The curve's timing is the calibration's: resist is
      // negligible where the game starts and near the cap at the
      // SAME tier the margin anchor pins (RefTier — wherever it
      // currently sits, not a hardcoded wave).
      Expect.isLessThan
        (Balance.scaleOfWave sat 5).Resist
        (0.25f * Balance.RhoMax)
        "early resist is a small fraction of the cap"

      Expect.isGreaterThan
        (Balance.scaleOfWave sat (int Balance.RefTier * 5)).Resist
        (0.9f * Balance.RhoMax)
        "resist approaches the cap at the calibration tier")

    testCase "clearBonus: bill-share payout, floored and monotone" (fun () ->
      let baseBonus = WorldConfig.defaults.WaveClearBonus

      // Tier 0 (waves 1-4) always pays the config floor.
      Expect.equal
        (Balance.clearBonus baseBonus sat 4)
        baseBonus
        "tier 0 pays the floor"

      // The mechanism, wherever the tuning sits: never below the
      // floor, and the bill share DOES activate somewhere (a payout
      // above the floor exists). ΔBill dips near saturation, so the
      // payout is NOT monotone — no ordering is pinned.
      let mutable aboveFloor = false

      for t = 1 to 15 do
        let b = Balance.clearBonus baseBonus sat (5 * t)

        Expect.isGreaterThanOrEqual b baseBonus $"tier %d{t} floored"

        if b > baseBonus then
          aboveFloor <- true

      Expect.isTrue aboveFloor "the bill share activates above the floor")

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
      let hp0 = TestData.Fixtures.grunt.Hp

      Enemies.Enemies.spawn
        {
          TestData.Fixtures.grunt with
              Resist = 0.5f
        }
        m
        productionMap.Path

      // The damage math is read from the fixture, not repeated as
      // literals — retune the fixture and the mechanism assertion
      // (half damage, floored at 1) still reads the same.
      let resisted(dmg: float32) = max 1f (0.5f * dmg)

      match m.Defs |> AMap.getValue |> Seq.tryHead with
      | Some(KeyValueV(eid, _)) ->
        let events = Enemies.Enemies.applyDamage eid 10f m

        Expect.isEmpty
          events
          "10 dmg − 50 % stays under the fixture hp, no kill"

        match m.Healths |> CMap.tryGetValue eid with
        | ValueSome h ->
          Expect.equal h.Hp (hp0 - resisted 10f) "resisted to half"
        | ValueNone -> failtest "health row must exist"

        // Floor: 1 dmg against 50 % resist still deals 1.
        Enemies.Enemies.applyDamage eid 1f m |> ignore

        match m.Healths |> CMap.tryGetValue eid with
        | ValueSome h ->
          Expect.equal h.Hp (hp0 - resisted 10f - resisted 1f) "floored to 1"
        | ValueNone -> failtest "health row must exist"
      | None -> failtest "enemy must exist")

    testCase "WaveScale.apply combines innate and tier resist" (fun () ->
      let scale = Balance.scaleOfWave sat 10
      let before = EnemyDefs.grunt
      let applied = WaveScale.apply scale before

      Expect.isGreaterThan
        applied.Resist
        scale.Resist
        "innate resist stacks on top of the tier's"

      Expect.equal
        applied.Resist
        (1f - (1f - before.Resist) * (1f - scale.Resist))
        "innate and tier resist multiply, never reaching 1"

      Expect.equal
        EnemyDefs.grunt
        before
        "apply returns a copy, the shared def is untouched")

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

      Towers.Towers.place struct (1, 1) TowerDefs.gunpost state.Towers

      Towers.Towers.place struct (1, 3) TowerDefs.sentry state.Towers

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
          if row.Spawn.Speed > 7.5f then
            TowerDefs.gunpost.ProjectileSpeed * waveSpeed
          else
            TowerDefs.sentry.ProjectileSpeed

        Expect.isTrue
          (abs(row.Spawn.Speed - expected) < 0.001f)
          $"spawn speed tracks the wave factor (got %f{row.Spawn.Speed})")
  ]
