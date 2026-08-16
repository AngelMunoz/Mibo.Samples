namespace Defli3D

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open Mibo.Layout
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// Application — the host-facing driver AND the Update phase. There
// is no Msg, no Cmd: `update` runs the nine systems in order;
// systems emit events as data, and update posts the handle*
// translations through the intent queue (`ctx.Intents.post`), so
// the sim never depends on the IntentQueue type. The runner drains
// the queue after Update and before the frame is forced, in post
// order. The cold paths (startNextWave / placeTower / upgradeTower /
// selectTower) are plain synchronous handlers; the build decisions
// (tile/occupancy/gold/cap) live in Placement — this file only
// translates an accepted plan into system calls. Windowed frontends
// deliver input through subscriptions that post intents.
//
// The event flow is one direction: systems emit, the handlers
// translate, and only ApplyDamage (→ Killed), FillWave (→
// SpawnEnemy), StartNextWave (→ WaveStarted) and the ticks emit —
// the cold-path handles return empty event lists, so nothing here
// recurses or re-enters the sim. Wave-fill (inside
// handleWaveEvents) and boss-split (inside handleEnemyEvents) stay
// same-call — tick-boundary invariants (see the migration plan).
//
// `program` composes the adaptive program the hosts run: init builds
// the frame force over the current state (boot runs host wiring
// first); update runs the sim and posts its reactions as intents.
// ─────────────────────────────────────────────────────────────

module Application =

  /// The grid cell CONTAINING a world position (floor of world/size) —
  /// the tile under the cursor. Mibo's Grid2DSpatial.worldToCell rounds
  /// to the NEAREST CENTER (a cursor in the right/bottom half of a tile
  /// picks the NEXT one — the outline visibly cuts tiles in half); the
  /// game wants the containing tile, so the pick is floor-based and
  /// bounds-checked. Origin-aware (the map origin is Zero).
  let inline cellAt
    (worldPos: Vector2)
    (grid: CellGrid2D<MapTile>)
    : struct (int * int) voption =
    // floor, not int: int truncates toward zero, which would map a
    // position just left of the origin into cell 0.
    let x = int(floor((worldPos.X - grid.Origin.X) / grid.CellSize.X))

    let y = int(floor((worldPos.Y - grid.Origin.Y) / grid.CellSize.Y))

    if x >= 0 && x < grid.Width && y >= 0 && y < grid.Height then
      ValueSome(struct (x, y))
    else
      ValueNone

  // ── Event translation (ex-Router handlers) ────────────────

  let handleEnemyEvents (state: State) (events: Enemies.EnemyEvent seq) : unit =
    for ev in events do
      match ev with
      | Enemies.Killed(eid, reward) ->
        let pos =
          state.Enemies.Positions
          |> CMap.tryGetValue eid
          |> ValueOption.defaultValue Vector2.Zero

        // The original router read the Defs row BEFORE its queued
        // Despawn Cmd ran (batch order); the direct handler must read
        // it before removing the row too — otherwise the boss split
        // dies with the corpse.
        let isBoss =
          state.Enemies.Defs
          |> CMap.tryGetValue eid
          |> ValueOption.exists(fun d -> d.Archetype = Boss)

        let struct (progress, pathIndex) =
          state.Enemies.Motions
          |> CMap.tryGetValue eid
          |> ValueOption.map(fun mv -> struct (mv.Progress, mv.PathIndex))
          |> ValueOption.defaultValue struct (0f, 0)

        Economy.Economy.earnGold reward state.Economy
        Enemies.Enemies.despawn eid state.Enemies

        Vfx.Vfx.burst Vfx.VfxKind.DeathPoof pos 0f state.Vfx

        // Boss split-on-death (Phase 6): grunts burst from the corpse.
        // Spawned SYNCHRONOUSLY (the FillWave-on-WaveStarted precedent):
        // a deferred round-trip would leave one frame with aliveCount =
        // 0 and the wave would clear before the children exist. Children
        // carry the wave's tier scale.
        if isBoss then
          let scale = AVal.getValue state.Waves.Scale
          let childDef = WaveScale.apply scale BossAura.SplitInto

          for i in 0 .. BossAura.SplitCount - 1 do
            // Small deterministic radial offsets so the children
            // don't stack on one spot (Defli's 16 px ÷ 64).
            let angle = float32 i / float32 BossAura.SplitCount * 2f * MathF.PI

            let childPos =
              pos + Vector2(MathF.Cos angle, MathF.Sin angle) * 0.25f

            Enemies.Enemies.spawnAt
              childDef
              childPos
              progress
              pathIndex
              state.Enemies
      | Enemies.ReachedBase _ ->
        let basePos = Cells.center state.Map.BaseCell (State.cellSize state)

        Economy.Economy.loseLife state.Economy
        Vfx.Vfx.burst Vfx.VfxKind.BaseHit basePos 0f state.Vfx
        Camera.Camera.shake 0.125f state.Camera

  let handleSpawnEvents
    (state: State)
    (events: Spawning.SpawnEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Spawning.SpawnEnemy def ->
        Enemies.Enemies.spawn def state.Enemies state.Map.Path
      | Spawning.SpawnFailed _ -> ()

  let handleWaveEvents (state: State) (events: Waves.WaveEvent seq) : unit =
    for ev in events do
      match ev with
      | Waves.WaveStarted wave ->
        // Fill the spawn queue IN THE SAME CALL: a deferred round-trip
        // would leave one frame where the wave is active with an empty
        // queue, and the clear check would fire instantly (the wave
        // starts and clears without spawning anything).
        let spawnEvents = Spawning.Spawning.fillWave wave state.Spawning

        handleSpawnEvents state spawnEvents
      | Waves.WaveCleared ->
        // Clear payout: ClearShare of the tier's equipment bill
        // (floored at the config base bonus) — one budget with the
        // kill rewards (see Balance). WaveNumber still holds the
        // wave that just cleared.
        let waveNumber = AVal.getValue state.Waves.WaveNumber

        let bonus =
          Balance.clearBonus
            state.Config.WaveClearBonus
            state.Waves.Saturation
            waveNumber

        Economy.Economy.earnGold bonus state.Economy

  let handleProjectileEvents
    (post: (unit -> unit) -> unit)
    (state: State)
    (events: Projectiles.ProjectileEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Projectiles.Impact impact ->
        // Damage batch: a DIRECT hit (pierce pass-through) damages
        // exactly its enemy; an AREA detonation fans one ApplyDamage
        // per enemy within ImpactRadius of the point (flat full
        // damage, no falloff). All ApplyDamage first, then the kills
        // are handled: despawns (Killed) never modify the Positions
        // view being enumerated here (the wave-13 ordering).
        let positions = state.Enemies.Positions |> AMap.getValue
        let events = ResizeArray<Enemies.EnemyEvent>()

        match impact.Enemy with
        | ValueSome eid ->
          events.AddRange(
            Enemies.Enemies.applyDamage eid impact.Damage state.Enemies
          )
        | ValueNone ->
          if impact.ImpactRadius > 0f then
            for KeyValueV(eid, epos) in positions do
              if Vector2.Distance(epos, impact.Pos) <= impact.ImpactRadius then
                events.AddRange(
                  Enemies.Enemies.applyDamage eid impact.Damage state.Enemies
                )

        // Kills + zone drop + burst post as one intent: the drain runs
        // them after this fan-out loop finishes, so despawns never
        // mutate the Positions view mid-enumeration. Bigger radii (and
        // zone droppers) read as explosions; the rest as small hits.
        let bigBurst =
          impact.ImpactRadius > 0.5f || ValueOption.isSome impact.Zone

        post(fun () ->
          handleEnemyEvents state events

          impact.Zone
          |> ValueOption.iter(fun z ->
            Zones.Zones.drop impact.Pos z state.Zones)

          Vfx.Vfx.burst
            (if bigBurst then
               Vfx.VfxKind.Explosion
             else
               Vfx.VfxKind.Impact)
            impact.Pos
            impact.Y
            state.Vfx)

  /// Zone ticks: declarative applications (damage + slow) translated
  /// into Enemies writes; kills fan out through the enemy handler.
  let handleZoneApplies (state: State) (applies: Zones.ZoneApply[]) : unit =
    for z in applies do
      if z.Damage > 0 then
        handleEnemyEvents
          state
          (Enemies.Enemies.applyDamage z.Enemy z.Damage state.Enemies)

      if z.SlowFactor < 1f then
        Enemies.Enemies.applySlow
          {
            Enemy = z.Enemy
            Factor = z.SlowFactor
            Seconds = z.SlowSeconds
          }
          state.Enemies

  let handleTowerEvents (state: State) (events: Towers.TowerEvent seq) : unit =
    for ev in events do
      match ev with
      | Towers.Fired shot ->
        // Projectile speed from the EFFECTIVE def (the upgrade
        // projection) — the +10 %/level upgrades must not be dropped
        // here. Bullet weapons (ProjectileSpeedScales) multiply by
        // the wave's speed factor so their lead prediction stays
        // exact against late-game mobs; loaders keep their raw speed
        // and eat the miss knob instead. One transient Scale read
        // per Fired batch (cold — same shape as the boss-split
        // handler). The shot's spawn point is the MUZZLE (Towers
        // offsets it along the firing line — the barrel end / the
        // deck's embrasure), so shots and muzzle VFX visibly leave
        // the gun.
        let waveSpeed = (AVal.getValue state.Waves.Scale).Speed

        let speed =
          state.Towers.Statics
          |> CMap.tryGetValue shot.Tower
          |> ValueOption.map(fun s ->
            let eff =
              state.Towers.EffectiveDef
              |> AMap.getValue
              |> ReadOnlyDict.tryGetValue shot.Tower
              |> ValueOption.defaultValue s.Def

            if eff.ProjectileSpeedScales then
              eff.ProjectileSpeed * waveSpeed
            else
              eff.ProjectileSpeed)
          |> ValueOption.defaultValue 0f

        let pos = shot.Muzzle

        // The target's hull-center Y at fire time (EnemyLayout.impactY)
        // — the flight's destination height. If the target's def row is
        // already gone (died earlier this frame), fall back to a
        // typical ground-hull center (0.35).
        let targetY =
          shot.Enemy
          |> ValueOption.bind(fun eid ->
            state.Enemies.Defs
            |> CMap.tryGetValue eid
            |> ValueOption.map EnemyLayout.impactY)
          |> ValueOption.defaultValue 0.35f

        // The volley: Volley shots fanned perpendicular to the firing
        // line (deterministic spread — no RNG). Each spawn flies its
        // own line to its own aim point with the trajectory's arc.
        for i = 0 to shot.Volley - 1 do
          let aim =
            if shot.Volley > 1 then
              let line = shot.Aim - pos
              let len = line.Length()

              let perp =
                if len > 0f then
                  Vector2(-line.Y, line.X) / len
                else
                  Vector2.UnitX

              let off =
                shot.Spread * ((float32 i / float32(shot.Volley - 1)) - 0.5f)

              shot.Aim + perp * off
            else
              shot.Aim

          let d = aim - pos
          let total = d.Length()

          let dir = if total > 0f then d / total else Vector2.UnitX

          Projectiles.Projectiles.spawn
            {
              Pos = pos
              Height = shot.Height
              TargetY = targetY
              Dir = dir
              TotalLen = total
              ArcHeight = Trajectory.arcHeight shot.Trajectory total
              Seek = shot.Seek
              Target = shot.Enemy
              Aim = aim
              Damage = shot.Damage
              ImpactRadius = shot.ImpactRadius
              Piercing = shot.Piercing
              Zone = shot.Zone
              Model = shot.ProjectileModel
              Scale = shot.ProjectileScale
              Speed = speed
            }
            state.Projectiles

        // Muzzle VFX: bow-style weapons puff dust; guns flash. The
        // burst spawns AT the muzzle (barrel end / embrasure) at the
        // shot's muzzle height — not the tower's center.
        let muzzleKind =
          if shot.MuzzleDust then
            Vfx.VfxKind.MuzzleDust
          else
            Vfx.VfxKind.Muzzle

        Vfx.Vfx.burst muzzleKind pos shot.Height state.Vfx

  // ── The per-frame sim ─────────────────────────────────────────────

  // ── Cold paths — plain handlers, called by the host ──────────────
  // Placement owns the build rules; these translate accepted plans.

  /// The player starts the next wave. No-op on game over.
  let inline startNextWave(state: State) : unit =
    if not(AVal.getValue state.Economy.GameOver) then
      let events = Waves.Waves.startNextWave state.Waves

      handleWaveEvents state events

  /// Places the selected tower at a cell. Placement owns the build
  /// rules (buildable tile, free cell, gold); this translates the
  /// accepted plan into system handles. Returns true when placed.
  let placeTower (state: State) (cell: struct (int * int)) : bool =
    match
      Placement.place
        state.Map
        state.Towers
        (AVal.getValue state.Economy.Gold)
        (AVal.getValue state.SelectedTower)
        cell
    with
    | ValueSome plan ->
      Towers.Towers.place plan.Cell plan.Def state.Towers
      Economy.Economy.spendGold plan.Cost state.Economy

      Vfx.Vfx.burst
        Vfx.VfxKind.Placement
        (Cells.center plan.Cell (State.cellSize state))
        0f
        state.Vfx

      true
    | ValueNone -> false

  /// Upgrades the tower under a cell. Placement owns the upgrade
  /// rules (level cap, gold); this translates the accepted plan into
  /// system handles. Returns true when upgraded.
  let upgradeTower (state: State) (cell: struct (int * int)) : bool =
    match
      Placement.upgrade state.Towers (AVal.getValue state.Economy.Gold) cell
    with
    | ValueSome plan ->
      Towers.Towers.upgrade plan.Tower state.Towers
      Economy.Economy.spendGold plan.Cost state.Economy
      true
    | ValueNone -> false

  /// Player switched the tower kind to place (cold path).
  let inline selectTower (state: State) (def: TowerDef) : unit =
    state.SelectedTower |> CVal.set def

  // ── Semantic actions — the InputMapper root ────────────────────
  // The mapper subscription (Input.subscriptions) builds an
  // ActionState from the input deltas and writes the Actions root
  // through the pre-step lane; this consumes it like the old keyboard
  // handler did. One-shots ride the Started edges (single-binding
  // actions — safe); the subscription clears the edges after update,
  // before the frame force, so a step with no new deltas reads an
  // edge-free state.
  //
  // PAN is a HELD query, not an edge: the root's Held is rebuilt from
  // hardware truth on every delta, so the direction is recomputed each
  // step from what is held RIGHT NOW — synonym bindings (A and Left
  // both map PanLeft) count once, and no missed edge can leave the pan
  // stuck (edge arithmetic add/subtract could not guarantee that).
  let private handleActions(cell: StateCell) : unit =
    let state = cell.Value
    let actions = state.Actions |> AVal.getValue
    let mutable restart = false

    for a in actions.Started do
      match a with
      | GameAction.StartNextWave -> startNextWave state
      | GameAction.SelectTower slot ->
        if slot >= 0 && slot < TowerDefs.slots.Length then
          selectTower state TowerDefs.slots[slot]
      | GameAction.ResetCamera -> Camera.Camera.reset state.Camera
      | GameAction.Restart -> restart <- true
      | GameAction.PanLeft
      | GameAction.PanRight
      | GameAction.PanUp
      | GameAction.PanDown -> ()

    // Pan direction: the sum of the held pan actions' steps
    // (non-pan actions contribute Zero).
    let mutable pan = Vector2.Zero

    for a in actions.Held do
      pan <- pan + Inputs.panStep a

    Camera.Camera.setKeyboardPan pan state.Camera

    // Restart stays in the game logic: swap the state in place. The
    // frame force reads the cell at force time, so the next force
    // re-binds the graph to the fresh state — no window/runner
    // re-create. Deferred to the end so the loops above never act on
    // a swapped-away state.
    if restart && AVal.getValue state.Economy.GameOver then
      cell.Value <- State.init state.Config

  // ── The adaptive program ─────────────────────────────────────────

  /// Builds the graph: the frame force reads the CURRENT state's
  /// projections at the end of every Step (a restart swaps the cell and
  /// the force re-binds on the next force). The force is a pure
  /// State → RenderFrame mapping — the clock is part of the state (the
  /// Clock root, written below in update).
  let inline init
    (cell: StateCell)
    (_ctx: AdaptiveFrameContext)
    : AdaptiveInit<Frame.RenderFrame> =
    AdaptiveInit.ofFrameBuilder(Frame.force(fun () -> cell.Value))

  /// The Update phase entry: runs the sim for the current state and
  /// posts its reactions through the runner's intent queue.
  let update
    (cell: StateCell)
    (ctx: AdaptiveContext)
    (gameTime: GameTime)
    : unit =
    // Semantic actions first (they used to run in the pre-step drain):
    // one-shots, pan edges, restart — restart may swap the cell, so
    // everything after re-reads it.
    handleActions cell

    let state = cell.Value
    // The draw side's clock — written every step, paused included, so
    // the frame always carries a fresh time (hover bob, idle spins).
    state.Clock.Set gameTime

    if not(AVal.getValue state.Paused) then
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let t0 = Diagnostics.tickStart()

      // Kimo's system organization: movement/"physics" first, then the
      // spawn/queue phases; read-only consumers after.
      let enemyEvents = Enemies.Enemies.tick dt state.Enemies state.Map.Path

      let spawnEvents = Spawning.Spawning.tick dt state.Spawning

      let waveEvents =
        Waves.Waves.tick
          state.Waves
          state.AliveCount
          (state.Spawning.Queue.Count = 0)

      let towerEvents =
        // Suppression's chain (BossPositions chooseA → per-tower
        // filter → count) settles bottom-up on read: the count and
        // filter nodes pull their sources when versions differ
        // (AdaptiveSlop fix #18 — dirty-indicator Version). Reading
        // the tail alone is fresh; no pre-read needed. Velocities
        // are the movement tick's plain rows (lead prediction).
        Towers.Towers.tick
          dt
          state.Towers
          state.Enemies.Alive
          state.Enemies.Velocities
          (state.Projections.Suppression |> AMap.getValue)
          (State.cellSize state)

      let projectileEvents =
        Projectiles.Projectiles.tick
          dt
          state.Projectiles
          (state.Enemies.Positions |> AMap.getValue)

      // Zones tick after the enemies they affect have moved: slow +
      // DoT applications come back as declarative data. The Alive
      // projection (not raw Positions): zone effects gate on the
      // enemy's archetype — Ground zones skip fliers.
      let zoneApplies = Zones.Zones.tick dt state.Zones state.Enemies.Alive

      Vfx.Vfx.tick dt state.Vfx
      Camera.Camera.tick dt state.Camera

      Diagnostics.tickEnd
        t0
        state.Diag
        state.AliveCount
        state.Spawning.Queue.Count

      // Reactions post as intents: the drain runs them after Update,
      // before the frame is forced — post order = the original Cmd
      // batch order. Empty batches post too: the handler's loop over
      // Array.empty is a no-op; probing emptiness would consume the
      // batch here and add nothing.
      ctx.Intents.post(fun () -> handleEnemyEvents state enemyEvents)
      ctx.Intents.post(fun () -> handleSpawnEvents state spawnEvents)
      ctx.Intents.post(fun () -> handleWaveEvents state waveEvents)
      ctx.Intents.post(fun () -> handleTowerEvents state towerEvents)

      ctx.Intents.post(fun () ->
        handleProjectileEvents ctx.Intents.post state projectileEvents)

      ctx.Intents.post(fun () -> handleZoneApplies state zoneApplies)

  /// The adaptive program: init builds the frame force and the
  /// subscription projection over the current state (boot runs host wiring
  /// first); update runs the sim; the sim's reactions drain as intents
  /// after Update.
  let inline program
    (boot: AdaptiveFrameContext -> unit)
    (cell: StateCell)
    (subscribe: AdaptiveFrameContext -> amap<SubId, AdaptiveSub>)
    : AdaptiveProgram<Frame.RenderFrame> =
    AdaptiveProgram.mkProgram
      (fun ctx ->
        boot ctx
        (init cell ctx) |> AdaptiveInit.withSubscriptions subscribe)
      (update cell)
