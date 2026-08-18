namespace Defli3D.State

open System
open System.Numerics
open Mibo.Layout
open Defli3D.State.Systems

// This file holds every difficulty and economy number in the game.
// Change a value here, then run Shared.Tests/BalanceTests.fs: it
// prints a per-wave table of HP, speed, reward and margin so you can
// see what the change does before playing.
//
// HOW DIFFICULTY SCALES
//
// Waves are grouped in tiers: tier = wave / 5 (waves 5-9 are tier 1,
// 10-14 are tier 2, ...). Waves 0-4 use the base defs unchanged.
//
// Enemy HP and resistance follow a logistic curve in the tier: slow
// growth at first, fast growth in the middle, then it levels off at
// a cap (the "saturation"). The cap is not a constant in this file.
// It is computed from the map itself (capacityOf below): how many
// cells you can build on and how much damage those cells can put on
// the road. Because the cap is tied to what the map can hold, a
// fully-built map always stays winnable. If the map changes (more
// obstacles, longer road, new seed), the cap is recomputed and the
// whole curve follows. Do not hardcode a cap or a buildable count
// here.
//
//   s(t) = sat / (1 + exp(-k (t - x0)))   HP multiplier, capped at sat
//   ρ(t) = RhoMax · sigmoid(k (t - x0))   damage reduction, same curve
//   v(t) = SpeedGrowth^t                  speed multiplier, never caps
//
// Speed is the only stat that never stops growing, so very late waves
// slowly get harder even after HP has leveled off.
//
// HOW INCOME SCALES
//
// Income follows the cost of keeping up, not the raw difficulty.
// Each tier we compute what a build that just barely holds the line
// would cost in gold:
//
//   Bill(t) = GoldPerPower · avgHpNormal · s(t) · v(t) / (1 - ρ(t))
//
// The tier's budget is Bill(t) - Bill(t-1): the extra gold the player
// must spend this tier to keep up. Kills pay KillShare of that budget,
// wave clears pay ClearShare of it. Because both pay from the same
// number, total income stays at a fixed ratio of what the player needs
// to spend, on any map. A richer map raises the bill and income rises
// with it. An earlier version paid kills proportional to HP growth and
// the economy flooded (20k gold by wave 20 while there was nothing
// left to buy). Do not go back to HP-based rewards.
//
// EARLY GAME SHAPE
//
// With the default pins (s(0) = 1, fastest growth at TierHalf), the
// first-tier HP multiplier comes out at roughly sat^(1/3): cap 27
// means wave 5 enemies have about 3x HP, cap 125 means about 5x. To
// set the wave-5 multiplier directly, use EarlyHpOverride and let the
// midpoint float instead.

module Balance =

  // ── Player-experience knobs ─────────────────────────────────

  /// How much stronger than "barely enough" the reference build is
  /// when the curve caps out. 1.0 would mean a perfectly built map
  /// exactly breaks even at RefTier, with zero room for mistakes.
  /// 0.65 leaves 35% headroom, because the math here counts single-
  /// target damage only and real builds also get splash, zones and
  /// slows, which playtesting put at roughly +50-100% real power.
  /// Raise it and the whole HP curve drops (easier). Lower it and
  /// the curve rises (harder).
  let Alpha = 0.65f

  /// How much of the map's total possible damage the reference player
  /// has actually built by the late game, as a fraction. 0.015 on the
  /// seed-42 grid (192 buildable cells) is about 10 maxed towers at
  /// good spots. This sets the HP cap: double it and the late-game HP
  /// cap roughly doubles. Check the balance test table after changing.
  let HorizonBuild = 0.015f

  /// The tier where HP grows fastest (the hard middle). 3 = around
  /// wave 15. Ignored when EarlyHpOverride is set.
  let TierHalf = 3f

  /// The tier the whole curve is calibrated against (around wave 30).
  /// At this tier the reference build's strength reads exactly Alpha.
  /// Move it later and the early game gets easier while the cap stays
  /// reachable; move it earlier and the curve steepens.
  let RefTier = 6f

  /// The most damage resistance a late enemy can have. 0.50 = late
  /// enemies take 50% less damage from everything.
  let RhoMax = 0.50f

  /// Gold price of one unit of damage-per-second-times-road-coverage.
  /// The tower defs price power at 0.56-1.28 (bulletdeck 0.56,
  /// gunpost 0.75, sentry 1.28), so 2 pays above the defs' own range:
  /// income comes out generous relative to build costs. Lower it
  /// toward 1 and every reward tightens.
  let GoldPerPower = 2f

  /// How much of a tier's extra build cost is paid back through wave
  /// clear bonuses (1 = the five clears of a tier refund exactly the
  /// tier's bill increase). Never drops below WorldConfig.WaveClearBonus.
  let ClearShare = 1f

  /// How much of a tier's extra build cost is paid back through kill
  /// gold (1.5 = kills refund 1.5x the tier's bill increase). Kills
  /// that round below 1 gold still pay 1.
  let KillShare = 1.5f

  /// Enemy speed per tier: 1.1 = +10% each tier, compounding, no cap.
  /// This is what keeps long games threatening after HP levels off.
  let SpeedGrowth = 1.1f

  /// Optional: fix the tier-1 (wave 5) HP multiplier to this value and
  /// let the curve's midpoint float. ValueNone = the midpoint is fixed
  /// at TierHalf and the wave-5 multiplier falls out of the curve.
  let EarlyHpOverride: float32 voption = ValueNone

  // ── Wave averages (derived from the defs, not tuned) ────────

  /// Average base HP of a normal wave. Normal waves spawn 4 grunts
  /// and 2 runners (composeWave), so this is their mix. Used to
  /// convert the tier's HP multiplier into a gold cost.
  let avgHpNormal: float32 =
    (EnemyDefs.grunt.Hp * 4f + EnemyDefs.runner.Hp * 2f) / 6f

  /// Average base gold reward per kill across the mixed late-game
  /// wave table (4 grunts, 2 runners, 1 tank, 1 flier). Used to turn
  /// the tier's kill budget into a per-kill reward multiplier.
  /// Bosses are paid separately and are not in this mix.
  let avgRewardMix: float32 =
    (float32 EnemyDefs.grunt.GoldReward * 4f
     + float32 EnemyDefs.runner.GoldReward * 2f
     + float32 EnemyDefs.tank.GoldReward
     + float32 EnemyDefs.flier.GoldReward)
    / 8f

  // ── The curves ──────────────────────────────────────────────

  let inline private sigma(x: float32) : float32 = 1f / (1f + MathF.Exp(-x))

  /// Picks the curve's steepness k and midpoint x0 from the cap and
  /// the active pin:
  ///   default  — s(0) = 1 and fastest growth at TierHalf
  ///   override — s(0) = 1 and s(1) = EarlyHpOverride
  let inline private steepness(sat: float32) : struct (float32 * float32) =
    match EarlyHpOverride with
    | ValueSome early ->
      let k = MathF.Log(sat - 1f) - MathF.Log(sat / early - 1f)
      struct (k, MathF.Log(sat - 1f) / k)
    | ValueNone -> struct (MathF.Log(sat - 1f) / TierHalf, TierHalf)

  /// (HP multiplier, speed multiplier, resistance) at tier t.
  let inline private curvesAt
    (sat: float32)
    (t: float32)
    : struct (float32 * float32 * float32) =
    let struct (k, x0) = steepness sat
    let x = k * (t - x0)

    struct (sat / (1f + MathF.Exp(-x)),
            MathF.Pow(SpeedGrowth, t),
            RhoMax * sigma x)

  /// Gold cost of a tier-t build that just barely holds the line:
  /// the damage the tier demands, converted to gold via GoldPerPower.
  let inline private billAt (sat: float32) (t: float32) : float32 =
    let struct (hp, v, rho) = curvesAt sat t
    GoldPerPower * avgHpNormal * hp * v / (1f - rho)

  /// All four multipliers for one wave. Pure function of the map cap
  /// and the wave number. Called once per wave from the Waves.Scale
  /// adaptive node and once per wave start from composeWave.
  let scaleOfWave (sat: float32) (number: int) : WaveScale =
    if number < 5 then
      // Waves 0-4 are tier 0 and use the base defs untouched. The
      // explicit 1f matters: the logistic value at t=0 lands one
      // float32 rounding step under 1 and would shave 1 HP off base
      // enemies when truncated to int.
      {
        Hp = 1f
        Speed = 1f
        Reward = 1f
        Resist = 0f
      }
    else
      let t = float32(max 0 (number / 5))
      let struct (hp, v, rho) = curvesAt sat t

      // Kill budget for the tier, divided by the base gold the
      // tier's enemies pay at reward 1 (tier t covers waves 5t..5t+4,
      // about 45+50t kills).
      let killBase = (45f + 50f * t) * avgRewardMix

      let reward = KillShare * (billAt sat t - billAt sat (t - 1f)) / killBase

      {
        Hp = hp
        Speed = v
        Reward = reward
        Resist = rho
      }

  /// Gold paid for clearing a wave: the tier's clear budget split
  /// over its 5 waves, never below the config base bonus so early
  /// waves keep their usual payout. `waveNumber` is the wave that
  /// just cleared.
  let clearBonus (baseBonus: int) (sat: float32) (waveNumber: int) : int =
    let t = float32(max 0 (waveNumber / 5))

    if t <= 0f then
      baseBonus
    else
      let share = ClearShare * (billAt sat t - billAt sat (t - 1f)) / 5f
      max baseBonus (int share)

  /// Same as billAt with an int tier. The tests read this to check
  /// income against build costs.
  let bill (sat: float32) (t: int) : float32 = billAt sat (float32(max 0 t))

  // ── The per-map scan (runs once at State.init) ──────────────

  /// What the map can hold, and the HP cap that falls out of it.
  type Capacity = {
    /// Cells you can build on. The hard cap on tower count.
    Buildable: int
    /// Cells of road. 1 cell = 1 world unit of walking.
    RoadCells: int
    /// Sum over all buildable cells of the best damage that cell
    /// could do: max over tower defs of [dps at level 5 x road cells
    /// in range]. Splash, zones and piercing are left out on purpose;
    /// they are the headroom that Alpha accounts for.
    PowerTotal: float32
    /// The HP cap s(infinity) = this. Computed from the other three,
    /// never set by hand. Tune Alpha and HorizonBuild instead.
    Saturation: float32
  }

  /// Measures the map and solves for the HP cap.
  ///
  /// For every buildable cell, counts how many road cells each tower
  /// def could cover from there (1 road cell = 1 unit of walking, so
  /// this approximates time-in-range without depending on waypoint
  /// spacing) and keeps the best def's damage x coverage. The sum is
  /// PowerTotal.
  ///
  /// The cap is the value that makes the reference build read exactly
  /// Alpha strength at RefTier:
  ///
  ///   sat · sigma(k (RefTier - x0)) =
  ///     HorizonBuild · PowerTotal · (1 - ρ(RefTier))
  ///     / (Alpha · v(RefTier) · avgHpNormal)
  ///
  /// k depends on sat and ρ(RefTier) depends on k, so we iterate a
  /// few rounds until it settles.
  let capacityOf(map: MapModel) : Capacity =
    // Grid origin is Zero and 1 cell = 1 unit, so cell centers sit
    // at (x + 0.5, y + 0.5).
    let inline roadCell (x: int) (y: int) =
      Vector2(float32 x + 0.5f, float32 y + 0.5f)

    let pathGrid = MapModel.pathGrid map
    let road = ResizeArray<Vector2>()

    for y = 0 to pathGrid.Height - 1 do
      for x = 0 to pathGrid.Width - 1 do
        match CellGrid2D.get x y pathGrid with
        | ValueSome tile when tile.IsPath -> road.Add(roadCell x y)
        | _ -> ()

    // Each def's squared range and max-level dps.
    let powers =
      TowerDefs.all
      |> Array.map(fun d ->
        let eff = TowerDefs.effectiveDef d 5

        struct (float32(d.Range * d.Range),
                eff.Warhead.Damage * eff.FireRate * float32 eff.Volley))

    let mutable buildable = 0
    let mutable powerTotal = 0f

    for y = 0 to pathGrid.Height - 1 do
      for x = 0 to pathGrid.Width - 1 do
        if MapModel.isBuildable x y map then
          buildable <- buildable + 1

          let c = roadCell x y
          let mutable best = 0f

          for struct (rangeSq, dps) in powers do
            let mutable exposure = 0

            for i = 0 to road.Count - 1 do
              if Vector2.DistanceSquared(c, road[i]) <= rangeSq then
                exposure <- exposure + 1

            best <- max best (dps * float32 exposure)

          powerTotal <- powerTotal + best

    // Cap at RefTier, before resistance is folded in.
    let vRef = MathF.Pow(SpeedGrowth, RefTier)
    let cRef = HorizonBuild * powerTotal / (Alpha * vRef * avgHpNormal)

    // Iterate: each round recomputes k from the current cap and the
    // resistance that k implies, then corrects the cap for both.
    let mutable sat = cRef + 1f

    for _ = 1 to 8 do
      let struct (k, x0) = steepness sat
      let rhoRef = RhoMax * sigma(k * (RefTier - x0))
      sat <- cRef * (1f - rhoRef) / sigma(k * (RefTier - x0))

    {
      Buildable = buildable
      RoadCells = road.Count
      PowerTotal = powerTotal
      Saturation = sat
    }
