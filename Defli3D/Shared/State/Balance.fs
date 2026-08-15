namespace Defli3D.State

open System
open System.Numerics
open Mibo.Layout
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// Balance — the difficulty/economy MODEL, one home for every
// tuning knob (see Shared.Tests/BalanceTests.fs: the margin table
// prints what each knob does before you commit to it).
//
// The shape: enemy difficulty grows LOGISTICALLY in the tier
// (t = wave/5) and saturates at a level CALIBRATED AGAINST THE
// MAP's capacity, so the game never becomes unwinnable through
// scaling alone — a perfect build stays above margin 1, an
// average build trades lives. Player power is bounded (finite
// buildable cells, level-5 cap), so enemy scaling must be bounded
// relative to it; that single constraint picks the function
// family.
//
//   s(t) = sat · σ(k·(t − x₀))      HP multiplier (logistic)
//   ρ(t) = RhoMax · σ(k·(t − x₀))   resistance, same shape/timing
//   v(t) = SpeedGrowth^t            speed multiplier, UNCAPPED
//   g(t) = KillShare·ΔBill(t)/killBase(t)   reward multiplier
//
// ECONOMY (the lesson of the first playtest): income must track the
// MARGINAL cost of staying at parity, never the demand LEVEL. The
// failed coupling g = s^0.8 paid kills at the demand level — on a
// saturation-52 map that meant 13.5× base rewards at tier 3 and a
// 20 k gold economy by wave 20, while the equipment bill was nearly
// flat. The bill anchoring instead pays, per tier, a fixed share of
// what the NEXT tier of margin-1 equipment costs:
//
//   D(t)     = s(t)·v(t)/(1−ρ(t))            demand ratio
//   Bill(t)  = GoldPerPower·avgHp·D(t)       gold a margin-1 build costs
//   ΔBill(t) = Bill(t) − Bill(t−1)           the tier's equipment bill
//   kills pay  KillShare·ΔBill  (per-kill max 1 gold floor in apply)
//   clears pay ClearShare·ΔBill  (floored at the config base bonus)
//
// Both income streams share one budget (Scarcity = the shares' sum,
// < 1 = slightly scarce by construction), so cumulative income
// tracks Bill(t) at a roughly constant ratio — never floods, never
// starves, on any map (a richer map raises ΔBill and income follows).
//
// MAP-AGNOSTIC BY CONTRACT: the saturation comes from THIS map's
// capacity scan (capacityOf, below). A map rework — more obstacles,
// longer roads, new seeds — recalibrates automatically. NEVER bake
// a saturation or buildable count into these constants.
//
// Margin model (the harness math): with towers firing continuously
// at the enemy stream, the enemy COUNT cancels — each extra enemy
// takes proportionally more fire — so
//
//   M(t) = η · PowerTotal · (1 − ρ(t)) / (v(t) · avgHp · s(t))
//
// where η is the player's fraction of the greedy capacity fill.
// Uncapped speed is the only asymptotic margin erosion (M decays
// like v(t_ref)/v(t) past saturation): a deliberate, printed
// choice — bullets track wave speed (TowerDef.ProjectileSpeedScales)
// and zones/AoE always land, so it stays a slow squeeze, not a
// cliff.
//
// The early-slope trade-off (math truth, useful when tuning): in
// the default mode (inflection pinned at TierHalf, s(0) = 1) the
// first-tier multiplier is roughly s(1) ≈ sat^(1/3). Saturation 27
// → wave 5 at ×3; saturation 125 → ×5. A gentler start forces a
// later inflection; set EarlyHpOverride to pin the start instead
// and let the inflection float.
// ─────────────────────────────────────────────────────────────

module Balance =

  // ── Universal knobs (player-experience constants) ──────────

  /// Margin target at RefTier for the horizon build. BELOW 1 on
  /// purpose: the margin model is single-target by contract, and
  /// playtest measured the AoE/zone/slow cushion at roughly
  /// +50-100 % felt power — the anchor absorbs the cushion, or
  /// every calibrated build steamrolls (0.70 = the reference build
  /// reads 0.70 single-target, ~1.1-1.4 felt).
  let Alpha = 0.70f

  /// Fraction of the map's full power the reference player fields
  /// when the curve saturates — calibrates the asymptote against a
  /// realistic endgame build, not the theoretical full map. On rich
  /// maps (the seed-42 grid: 192 buildable cells) this reads as the
  /// chokepoint clusters plus a support line, NOT the whole map:
  /// 0.05 there ≈ 10 maxed towers. Read the margin table before
  /// changing it — the saturation (and with it the wave-10/15
  /// multipliers) scales almost linearly with this knob.
  let HorizonBuild = 0.015f

  /// The logistic's inflection tier — the designed "hard middle".
  /// 3 = wave 15. Ignored when EarlyHpOverride is set.
  let TierHalf = 4f

  /// The calibration tier (~wave 30-34): the anchor where the
  /// horizon build's margin reads exactly Alpha.
  let RefTier = 7f

  /// Resistance cap (multiplicative fraction, same logistic shape
  /// as HP). 0.35 = late enemies take 35 % less from every source.
  let RhoMax = 0.25f

  /// Gold per dps×exposure power unit, from the def store
  /// (bulletdeck 0.56, gunpost 0.75, sentry 1.28 — the efficient
  /// end of the range). Converts the demand ratio into the gold a
  /// margin-1 build costs.
  let GoldPerPower = 1f

  /// Share of each tier's equipment bill paid by wave CLEARS
  /// (floored at WorldConfig.WaveClearBonus per wave — the stable
  /// sustenance late, when kill rewards shrink with the bill).
  let ClearShare = 2f

  /// Share of each tier's equipment bill paid by KILLS (the per-
  /// kill max 1 gold floor in WaveScale.apply keeps small kills
  /// from feeling pointless).
  let KillShare = 1.2f

  /// Speed growth per tier, UNCAPPED by design (see module header).
  let SpeedGrowth = 1.07f

  /// Optional: pin the first-tier HP multiplier instead of the
  /// inflection tier (s(1) = this value, s(0) = 1; x₀ floats).
  /// ValueNone = default mode (inflection at TierHalf).
  let EarlyHpOverride: float32 voption = ValueNone

  // ── Def-store mix constants (the demand/income side) ────────

  /// The normal-wave enemy mix (composeWave's else branch: grunt 4
  /// / runner 2) — the demand side's average base HP.
  let avgHpNormal: float32 =
    (float32 EnemyDefs.grunt.Hp * 4f + float32 EnemyDefs.runner.Hp * 2f) / 6f

  /// Representative wave mix for income (grunt 4 / runner 2 / tank 1
  /// / flier 1 — the mod-3/4 tables' blend) — the base gold one
  /// enemy pays at reward ×1. Bosses are accounted separately.
  let avgRewardMix: float32 =
    (float32 EnemyDefs.grunt.GoldReward * 4f
     + float32 EnemyDefs.runner.GoldReward * 2f
     + float32 EnemyDefs.tank.GoldReward
     + float32 EnemyDefs.flier.GoldReward)
    / 8f

  // ── The curves ──────────────────────────────────────────────

  let inline private sigma(x: float32) : float32 = 1f / (1f + MathF.Exp(-x))

  /// (k, x₀) for a given saturation, from the active pin pair:
  ///   default  — s(0) = 1 and inflection = TierHalf
  ///              (k = ln(sat−1)/TierHalf; s(RefTier) = sat−1 exactly)
  ///   override — s(0) = 1 and s(1) = EarlyHpOverride
  let inline private steepness(sat: float32) : struct (float32 * float32) =
    match EarlyHpOverride with
    | ValueSome early ->
      let k = MathF.Log(sat - 1f) - MathF.Log(sat / early - 1f)
      struct (k, MathF.Log(sat - 1f) / k)
    | ValueNone -> struct (MathF.Log(sat - 1f) / TierHalf, TierHalf)

  /// The tier's curves at fractional tier t: (s, v, ρ).
  let inline private curvesAt
    (sat: float32)
    (t: float32)
    : struct (float32 * float32 * float32) =
    let struct (k, x0) = steepness sat
    let x = k * (t - x0)

    struct (sat / (1f + MathF.Exp(-x)),
            MathF.Pow(SpeedGrowth, t),
            RhoMax * sigma x)

  /// The gold a margin-1 tier-t build costs (the equipment bill):
  /// demand ratio × power-to-gold conversion.
  let inline private billAt (sat: float32) (t: float32) : float32 =
    let struct (hp, v, rho) = curvesAt sat t
    GoldPerPower * avgHpNormal * hp * v / (1f - rho)

  /// The tier's multiplier set — replaces the old WaveScale.ofWave.
  /// Pure: a function of the map-derived saturation and the wave
  /// number. Lives inside the Waves.Scale adaptive node (recomputes
  /// once per wave) and composeWave (once per wave start).
  let scaleOfWave (sat: float32) (number: int) : WaveScale =
    if number < 5 then
      // Tier 0 = the base defs, EXACTLY unscaled (the s(0) = 1 pin,
      // explicit: the emergent logistic value sits a float32
      // rounding step below 1 and would truncate base HP).
      {
        Hp = 1f
        Speed = 1f
        Reward = 1f
        Resist = 0f
      }
    else
      let t = float32(max 0 (number / 5))
      let struct (hp, v, rho) = curvesAt sat t

      // The reward multiplier: KillShare of the tier's equipment
      // bill, spread over the base gold the tier's kills would pay
      // (tier t covers waves 5t..5t+4 → (45+50t) enemies × mix).
      let killBase = (45f + 50f * t) * avgRewardMix

      let reward = KillShare * (billAt sat t - billAt sat (t - 1f)) / killBase

      {
        Hp = hp
        Speed = v
        Reward = reward
        Resist = rho
      }

  /// The per-wave clear payout: ClearShare of the tier's equipment
  /// bill (spread over its 5 waves), floored at the config base
  /// bonus so early waves keep the classic feel. `waveNumber` is
  /// the wave that just CLEARED.
  let clearBonus (baseBonus: int) (sat: float32) (waveNumber: int) : int =
    let t = float32(max 0 (waveNumber / 5))

    if t <= 0f then
      baseBonus
    else
      let share = ClearShare * (billAt sat t - billAt sat (t - 1f)) / 5f
      max baseBonus (int share)

  /// The gold a margin-1 tier-t build costs — the economy's anchor
  /// (harness table + budget assertions read this).
  let bill (sat: float32) (t: int) : float32 = billAt sat (float32(max 0 t))

  // ── The per-map capacity scan (cold: once per State.init) ───

  /// The map's difficulty ceiling and the calibrated saturation.
  type Capacity = {
    /// Buildable cells — the hard cap on tower count.
    Buildable: int
    /// Road cells (the traversable length, 1 cell = 1 unit).
    RoadCells: int
    /// Σ over buildable cells of the best single-target power the
    /// cell can hold: max over defs of [ dps(L5) × exposure(R) ].
    /// AoE, zones and piercing are the margin cushion — excluded
    /// on purpose.
    PowerTotal: float32
    /// The logistic's asymptote: s(∞) = this. Derived, never tuned
    /// directly — tune Alpha/HorizonBuild instead.
    Saturation: float32
  }

  /// Scans the map: exposure of every buildable cell (road-cell
  /// centers within each def's range — each road cell ≈ 1 unit of
  /// traversal, matching Towers.tick's exact-distance check closely
  /// and independent of the path's waypoint density), folds the
  /// best per-cell single-target power at level 5, and solves the
  /// saturation so the horizon build's margin reads exactly Alpha
  /// at RefTier:
  ///
  ///   sat · σ(k·(RefTier − x₀)) = HorizonBuild · PowerTotal
  ///                              · (1 − ρ(RefTier))
  ///                              / (Alpha · v(RefTier) · avgHp)
  ///
  /// The ρ(RefTier) term needs k which needs sat — a mild coupling,
  /// settled by a short fixed-point sweep (cold path, floats).
  let capacityOf(map: MapModel) : Capacity =
    // 1 cell = 1 world unit, grid origin Zero → centers at (x+0.5).
    let roadCell (x: int) (y: int) =
      Vector2(float32 x + 0.5f, float32 y + 0.5f)

    let pathGrid = MapModel.pathGrid map
    let road = ResizeArray<Vector2>()

    for y = 0 to pathGrid.Height - 1 do
      for x = 0 to pathGrid.Width - 1 do
        match CellGrid2D.get x y pathGrid with
        | ValueSome tile when tile.IsPath -> road.Add(roadCell x y)
        | _ -> ()

    // Best single-target DPS × range per def at the level cap.
    let powers =
      TowerDefs.all
      |> Array.map(fun d ->
        let eff = TowerDefs.effectiveDef d 5

        struct (float32(d.Range * d.Range),
                float32 eff.Damage * eff.FireRate * float32 eff.Volley))

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

    // Saturation: the fixed point of the margin pin at RefTier.
    let vRef = MathF.Pow(SpeedGrowth, RefTier)
    let cRef = HorizonBuild * powerTotal / (Alpha * vRef * avgHpNormal)

    // Start from the resist-free value, then settle the ρ↔k coupling.
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
