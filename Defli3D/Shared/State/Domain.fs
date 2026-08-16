namespace Defli3D.State

open System.Numerics
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Typed IDs (units of measure — zero-cost, struct-friendly)
// ─────────────────────────────────────────────────────────────

[<Measure>]
type EnemyId

[<Measure>]
type TowerId

[<Measure>]
type ProjectileId

[<Measure>]
type ZoneId

// ─────────────────────────────────────────────────────────────
// Models (one baked GLB entry — see Models.fs, GENERATED)
// ─────────────────────────────────────────────────────────────

/// One baked 3D model entry (name + asset path + mesh-local extents).
/// GENERATED data lives in Models.fs — the dataset is compile-time.
/// Sizes are real meters: ground tiles are 1.0 × 0.2 × 1.0, built
/// towers 1×1×1, enemies ~1 wide, ammo 0.16–0.32. The sim carries
/// ModelInfo values as backend-neutral asset identities; the views
/// resolve meshes at the edge (asset handles are presentation state).
[<Struct>]
type ModelInfo = {
  Name: string
  Path: string
  SizeX: float32
  SizeY: float32
  SizeZ: float32
}

// ─────────────────────────────────────────────────────────────
// Map
// ─────────────────────────────────────────────────────────────

[<Struct>]
type TerrainKind =
  | Grass
  | Dirt
  | Stone
  | Sand

[<Struct>]
type MapTile = {
  Terrain: TerrainKind
  IsPath: bool
  Buildable: bool
  /// True on the path's waypoint cells (spawn/base — Waypoints layer).
  IsWaypoint: bool
  /// Decorations-layer model to draw over the terrain (ValueNone =
  /// no decoration on this cell).
  Decoration: ModelInfo voption
}

// ─────────────────────────────────────────────────────────────
// World config (assembled outside the state — Kimo Phase 6 seam)
// ─────────────────────────────────────────────────────────────

/// Level-1 hand-authored road (fixed waypoints) vs Level-2 procedural
/// (props scattered, road carved by findPath, floodFill validated).
[<Struct>]
type MapVariant =
  | HandAuthored
  | Procedural

type WorldConfig = {
  Seed: int
  StartingGold: int
  StartingLives: int
  WaveClearBonus: int
  GridCols: int
  GridRows: int
  MapVariant: MapVariant
}

module WorldConfig =

  let defaults = {
    Seed = 42
    StartingGold = 80
    StartingLives = 20
    WaveClearBonus = 25
    GridCols = 20
    GridRows = 20
    MapVariant = MapVariant.Procedural
  }

// ─────────────────────────────────────────────────────────────
// Enemy definitions & components (code-authored def store)
// ─────────────────────────────────────────────────────────────

[<Struct>]
type EnemyArchetype =
  | Grunt
  | Runner
  | Tank
  | Flier
  /// Slow, huge HP, debuffs nearby towers (BossAura), splits on death.
  /// Walks the road — no custom locomotion.
  | Boss

/// What kinds of enemies a weapon or zone may engage. Ground = road
/// walkers (cannons and catapults — their arcing shot cannot track
/// a flying target); Air = fliers only (no current preset — the
/// flak-shaped future); Any = both (arrows, bullets, rockets).
[<Struct>]
type TargetDomain =
  | Ground
  | Air
  | Any

module TargetDomain =

  /// Does this domain cover the archetype? THE one place the
  /// air/ground eligibility rule lives — targeting and zone
  /// application both ask here, never inline.
  let inline covers (domain: TargetDomain) (archetype: EnemyArchetype) : bool =
    match domain with
    | Any -> true
    | Ground -> archetype <> EnemyArchetype.Flier
    | Air -> archetype = EnemyArchetype.Flier

[<Struct>]
type EnemyDef = {
  Key: string
  Archetype: EnemyArchetype
  Hp: int
  /// World units per second (1 cell = 1 unit; Defli's px/s ÷ 64).
  Speed: float32
  /// Fraction of incoming damage negated (0.35 = 35 % less). Base
  /// defs carry 0; the wave tier writes its resistance here at spawn
  /// (WaveScale.apply), so every damage source — direct hits, area
  /// detonations, zone ticks — pays the same multiplicative tax.
  Resist: float32
  GoldReward: int
  /// Hull model from the tower-defense kit (enemy-ufo-*).
  HullModel: ModelInfo
  /// Weapon model mounted on the hull, aimed at the heading by the
  /// view (ValueNone = no weapon mount). No current def sets it —
  /// the UFOs float weaponless; kept for future defs.
  WeaponModel: ModelInfo voption
  /// Uniform render scale (boss = the grunt hull at 1.6×).
  Scale: float32
}

// NOTE: the def STORES (module EnemyDefs / module BossAura /
// module TowerDefs) live at the top of State/Systems/Map.fs —
// they bind Models.fs values (enemy-ufo-a, weapon-ballista, …) and
// Models.fs compiles after this file (it needs ModelInfo), so the
// stores sit in the first post-Models slot. Same namespace
// (Defli3D.State), so every consumer sees them exactly as if they
// were declared here.

/// Per-enemy components (rows in the Enemies sub-system's CMaps).
[<Struct>]
type Health = { Hp: int; MaxHp: int }

[<Struct>]
type Motion = {
  Speed: float32
  Slow: float32
  Progress: float32
  PathIndex: int
}

/// One wave's executable content — composed by Waves (director),
/// executed by Spawning (queue + weighted picks).
[<Struct>]
type WaveDef = {
  Table: struct (EnemyDef * int)[]
  Count: int
  Interval: float32
  InitialDelay: float32
  /// Explicit spawns at fixed delays, queued AHEAD of the weighted
  /// picks (Phase 6: the boss leads its wave deterministically — a
  /// table entry would make it a dice roll). Empty for regular waves.
  ExtraSpawns: struct (EnemyDef * float32)[]
}

/// Join row of the EnemyViews projection (Positions × Healths ×
/// Motions × Defs). Positions are logical XZ-plane coordinates
/// (x → x, y → z at the render edge).
[<Struct>]
type EnemyView = {
  Pos: Vector2
  Hp: int
  MaxHp: int
  /// The enemy's archetype — the air/ground bit (TargetDomain.covers)
  /// for targeting and zone application.
  Archetype: EnemyArchetype
  Progress: float32
  Slow: float32
  PathIndex: int
}

// ─────────────────────────────────────────────────────────────
// Tower definitions & components
// ─────────────────────────────────────────────────────────────

/// Which enemy a tower picks first among its in-range candidates.
[<Struct>]
type TargetPolicy =
  /// Closest to the base (highest progress).
  | First
  /// Furthest from the base (lowest progress).
  | Last
  /// Highest max HP.
  | Strongest
  /// Lowest current HP.
  | Weakest
  /// Nearest to the tower.
  | Closest

/// How the tower body is built and where its weapon lives. The kit's
/// pieces dictate the mounts (see TowerLayout for the per-chassis
/// bodies — every chassis is COMPLETE from placement: a level-up is
/// power, not height):
///   Emplacement — round base pad, gun mounted directly on it.
///   Deck of letter — round modular tower whose MIDDLE (letter
///     0/1/2 = a/b/c) is the rotating gun deck: a fires arrows, b
///     cannons, c bullets. The letter is part of the DEF and styles
///     every piece of the tower (an "a" tower is all a-parts). No
///     separate gun model — the embrasures are the weapon.
///   Bunker — square modular tower: bottom → open bay (gun inside,
///     any but catapult — the arm needs swing clearance) → top cover.
///     The ONLY chassis allowed a middle section with a big gun.
///   Keep of letter — prebuilt self-armed tower (build-a/b/c): a
///     fires an arrow volley (static), b its cannon through the
///     opening (the whole tower rotates), c bullet volleys from its
///     four openings (static).
///   Battery — round heavy-gun platform (catapults, large ballistas):
///     base pad + bottom + battlement top, NO middle (middles are
///     gun decks — big guns sit on open tops instead).
[<Struct>]
type Chassis =
  | Emplacement
  | Deck of letter: int
  | Bunker
  | Keep of letter: int
  | Battery

/// The shot's flight shape. XZ is always a straight line to the aim
/// point — the trajectory only shapes Y:
///   Flat    — lerp muzzle height → target height (bullets/arrows).
///   SemiArc — a low arc whose height grows with distance (cannon).
///   Arc     — a full high parabola (catapult).
[<Struct>]
type Trajectory =
  | Flat
  | SemiArc
  | Arc

module Trajectory =

  /// The arc's apex height above the muzzle→target lerp line, from
  /// the flight distance (cannon arcs grow with range, catapults fly
  /// a fixed high lob).
  let inline arcHeight (traj: Trajectory) (dist: float32) : float32 =
    match traj with
    | Flat -> 0f
    | SemiArc -> min 0.45f (dist * 0.18f)
    | Arc -> min 1.4f (0.35f + dist * 0.25f)

/// When projectiles from this weapon home in on their live target.
/// Dumbfire shots never correct after the fire-time lead prediction;
/// seeking shots chase the target every tick instead.
///   Never        — dumbfire at every level (ballistas, cannons,
///                   catapults: their ammo is a dumb chunk).
///   FromLevel n  — seeks once the tower reaches level n (guns: a
///                   trained crew walks the tracers onto the target).
///   Always       — seeks from level 1 (rockets).
[<Struct>]
type HomingPolicy =
  | Never
  | FromLevel of int
  | Always

/// A lasting ground effect applied at the impact point (catapult and
/// cannon shells): enemies inside are slowed and ticked damage while
/// the zone lives. Multiple zones may affect one enemy, stacking up
/// to MaxStacks sources. Affects gates BOTH the DoT and the slow by
/// air/ground — arrow patches (Affects = Any) catch fliers, cannon
/// and catapult residue (Ground) does not.
[<Struct>]
type ZoneDef = {
  Radius: float32
  Seconds: float32
  /// Speed factor while inside (0.6 = 40 % slower).
  Slow: float32
  /// Damage per tick while inside.
  TickDamage: int
  /// Seconds between damage ticks.
  TickInterval: float32
  /// How many concurrent zones may affect one enemy.
  MaxStacks: int
  /// Which enemies the zone's effects may touch.
  Affects: TargetDomain
}

/// One placeable tower preset (a chassis × weapon bundle). Keys 1-0
/// select from TowerDefs.slots — the def IS the bundle.
[<Struct>]
type TowerDef = {
  Key: string
  Name: string
  Chassis: Chassis
  Cost: int
  /// Range in grid cells — 1 cell = 1 world unit, so this is also the
  /// world-space range (Chebyshev ring narrowed by exact distance).
  Range: int
  Damage: int
  /// Shots per second (bigger guns fire slower — the reload pause IS
  /// the weapon's identity).
  FireRate: float32
  /// Fire-rate growth per level above 1 (0.1 = +10 %/level). Guns
  /// scale steeply (0.7) so they start as slow muskets and end as
  /// today's rapid fire; loaders stay near-flat — a veteran crew
  /// reloads a bit faster, never machine-gun fast.
  RatePerLevel: float32
  /// World units per second.
  ProjectileSpeed: float32
  /// Projectile speed multiplies by the wave's speed factor — bullets
  /// keep pace with late-game mobs (their lead prediction stays
  /// exact; only turns still dodge). Loaders stay dumb chunks and eat
  /// the full miss knob instead.
  ProjectileSpeedScales: bool
  /// Projectiles per shot: > 1 fans a volley around the aim point.
  Volley: int
  /// Volley spread radius (world units) around the aim point.
  Spread: float32
  Trajectory: Trajectory
  /// Damage radius at the impact point — every weapon hits an area.
  ImpactRadius: float32
  /// Piercing shot flies THROUGH enemies, damaging each it passes
  /// (large ballista).
  Piercing: bool
  /// Rocket: always seeks its target and explodes on impact (large
  /// turret).
  Homing: HomingPolicy
  /// Lasting ground effect applied at the impact point.
  Zone: ZoneDef voption
  /// Gun model mounted by gun-carrying chassis (ValueNone for keeps —
  /// they are self-armed; the ammo just comes out of the tower).
  WeaponModel: ModelInfo voption
  /// Visual scale of the mounted gun (1 = normal; the large guns
  /// read larger, e.g. 1.7).
  GunScale: float32
  /// Projectile model (the shell/arrow/bullet in flight).
  ProjectileModel: ModelInfo
  /// View scale for in-flight ammo (volley arrows/bullets are small).
  ProjectileScale: float32
  /// Bow-style weapons puff dust instead of a muzzle flash.
  MuzzleDust: bool
  /// What kinds of enemies this weapon may engage (cannons and
  /// catapults: Ground — walkers only).
  Targets: TargetDomain
  TargetPolicy: TargetPolicy
  /// Gold cost per upgrade level (flat) and the level cap.
  UpgradeCost: int
  MaxLevel: int
}

/// Per-tower components (rows in the Towers sub-system's CMaps).
/// Static vs runtime is the write-frequency grouping: Statics is written
/// once (placement), Runtimes every tick (cooldown/target/aim).
[<Struct>]
type TowerStatic = {
  Def: TowerDef
  Cell: struct (int * int)
}

[<Struct>]
type TowerRuntime = {
  Cooldown: float32
  Target: int<EnemyId> voption
  /// The current target's live position — the TowerAim projection
  /// exposes it so rotating chassis (decks, keep-b, guns) track the
  /// sim's ACTUAL target (not the view's nearest-enemy guess).
  Aim: Vector2 voption
}

// ─────────────────────────────────────────────────────────────
// Projectiles — ballistic flight model (fire at a PREDICTED point;
// seek follows the def's HomingPolicy: guns from level 4, rockets
// always, loaders never)
// ─────────────────────────────────────────────────────────────

/// One in-flight shot (a row in Projectiles.Rows). XZ flies a
/// straight line along Dir toward Aim; Y follows the trajectory
/// shape (lerp muzzle→target + ArcHeight·4t(1−t), t = Traveled /
/// TotalLen). Seeking shots (per the firing def's HomingPolicy)
/// re-aim Dir at the target's live position each tick; dumbfire
/// shots never correct — they detonate at (Aim, TargetY) whether
/// or not the enemy is still there, so fast targets genuinely dodge.
[<Struct>]
type ProjectileRow = {
  Pos: Vector2
  /// The shot's current flight height.
  Y: float32
  /// Unit flight direction (XZ).
  Dir: Vector2
  /// World units per second.
  Speed: float32
  /// Distance flown so far (drives the arc's t and dumbfire impact).
  Traveled: float32
  /// Muzzle→aim distance, frozen at spawn (dumbfire total length).
  TotalLen: float32
  /// The muzzle's world Y at fire time (arc origin).
  MuzzleY: float32
  /// The destination height (target hull center / ground).
  TargetY: float32
  /// Arc apex height above the lerp line (0 = flat).
  ArcHeight: float32
  /// Level-4+ unlock / rocket: chase the live target.
  Seek: bool
  Target: int<EnemyId> voption
  /// The predicted impact point this shot flies to.
  Aim: Vector2
  Damage: int
  /// Damage radius at impact — every weapon hits an area.
  ImpactRadius: float32
  /// Piercing shots damage each enemy they pass through and only
  /// expire on range/lifetime (HitIds prevents re-hits; null when
  /// not piercing — structs copy by value, the list is identity).
  Piercing: bool
  HitIds: ResizeArray<int<EnemyId>>
  /// Lasting ground effect applied at the impact point.
  Zone: ZoneDef voption
  Lifetime: float32
  /// Shell model + view scale (volley ammo is small).
  Model: ModelInfo
  Scale: float32
}

/// Spawn intent for a projectile — the row minus the id and the
/// lifetime (both system-owned: the id is assigned at spawn, the
/// lifetime is a fixed constant).
[<Struct>]
type ProjectileSpawn = {
  Pos: Vector2
  /// World-space Y of the muzzle at fire time — the flight origin
  /// (TowerLayout.muzzleY via TowerShot.Height).
  Height: float32
  TargetY: float32
  Dir: Vector2
  TotalLen: float32
  ArcHeight: float32
  Seek: bool
  Target: int<EnemyId> voption
  Aim: Vector2
  Damage: int
  ImpactRadius: float32
  Piercing: bool
  Zone: ZoneDef voption
  Model: ModelInfo
  Scale: float32
  Speed: float32
}

/// Difficulty scaling per wave tier (every 5 waves). The VALUES come
/// from Balance.scaleOfWave — logistic curves calibrated against the
/// map's capacity (see State/Balance.fs). This struct is the tier's
/// multiplier set carried on the wave state.
[<Struct>]
type WaveScale = {
  Hp: float32
  Speed: float32
  Reward: float32
  /// Fraction of incoming damage negated this tier (multiplicative,
  /// capped well below 1 — the game never becomes unwinnable through
  /// resistance alone).
  Resist: float32
}

module WaveScale =

  let inline apply (scale: WaveScale) (def: EnemyDef) : EnemyDef = {
    def with
        Hp = max 1 (int(float def.Hp * float scale.Hp))
        Speed = def.Speed * scale.Speed
        Resist = scale.Resist
        GoldReward = max 1 (int(float def.GoldReward * float scale.Reward))
  }

/// A tower shot (TowerEvent.Fired payload) — ONE trigger pull: the
/// full weapon description plus the firing solution (predicted aim
/// point, resolved by Towers from the target's velocity). Application
/// fans this out into `Volley` projectile spawns spread around Aim.
[<Struct>]
type TowerShot = {
  Tower: int<TowerId>
  /// The tracked target (ValueSome for seeking shots; dumbfire
  /// shots carry it too so a dead-target fallback aim exists).
  Enemy: int<EnemyId> voption
  /// The predicted impact point (target pos + velocity × flight).
  Aim: Vector2
  /// The muzzle's WORLD XZ at fire time — offset from the tower
  /// center along the firing line (the gun's barrel end / the deck's
  /// embrasure), so shots and muzzle VFX leave the barrel, not the
  /// tower's middle.
  Muzzle: Vector2
  Damage: int
  ImpactRadius: float32
  Piercing: bool
  /// Level-4+ unlock / rocket, resolved by Towers from the effective
  /// def at fire time.
  Seek: bool
  Volley: int
  Spread: float32
  Trajectory: Trajectory
  Zone: ZoneDef voption
  ProjectileModel: ModelInfo
  ProjectileScale: float32
  /// World-space Y of the muzzle at fire time (presentation payload —
  /// TowerLayout.muzzleY).
  Height: float32
  /// Bow-style weapons puff dust instead of a flash.
  MuzzleDust: bool
}

/// A projectile impact (ProjectileEvent.Impact payload). An AREA
/// detonation (Enemy = ValueNone) fans one ApplyDamage per enemy
/// within ImpactRadius of Pos; a DIRECT hit (Enemy = ValueSome —
/// pierce pass-throughs) damages exactly that enemy. Both spawn the
/// lasting Zone when the weapon carries one.
[<Struct>]
type ProjectileImpact = {
  Projectile: int<ProjectileId>
  Enemy: int<EnemyId> voption
  Pos: Vector2
  /// The detonation height (the arc's Y at arrival).
  Y: float32
  Damage: int
  ImpactRadius: float32
  Zone: ZoneDef voption
}

/// A slow application (Enemies.applySlow input) — factor + expiry.
[<Struct>]
type SlowApply = {
  Enemy: int<EnemyId>
  Factor: float32
  Seconds: float32
}

/// Render row of the state-owned Homing projection (Projectiles.Rows
/// mapped to view data): the shot's live position/height, flight
/// direction (view orients the model along it) and downscaled model.
[<Struct>]
type HomingView = {
  Pos: Vector2
  Y: float32
  Dir: Vector2
  Model: ModelInfo
  Scale: float32
}

// ─────────────────────────────────────────────────────────────
// Zones — lasting ground effects (slow + damage over time)
// ─────────────────────────────────────────────────────────────

/// One live ground zone (a row in Zones.Rows): applied at a
/// projectile's impact point, ticks its slow + damage while it
/// lives, drawn as a ground ring by the views.
[<Struct>]
type ZoneRow = {
  Pos: Vector2
  Def: ZoneDef
  /// Remaining life (seconds).
  Remaining: float32
  /// Countdown to the next damage tick.
  TickTimer: float32
}

/// Render row for zones (the frame carries the rows directly —
/// flat data, no cross-system join needed).
[<Struct>]
type ZoneView = {
  Pos: Vector2
  Radius: float32
  Remaining: float32
  Seconds: float32
}

// ─────────────────────────────────────────────────────────────
// Placement preview (hover highlight state)
// ─────────────────────────────────────────────────────────────

[<Struct>]
type PlacementStatus =
  | Hidden
  | Blocked
  | Affordable
  | TooExpensive

// ─────────────────────────────────────────────────────────────
// Cell helpers
// ─────────────────────────────────────────────────────────────

module Cells =

  /// World-space center of a grid cell (grid origin is Zero,
  /// cell size is uniform — see MapModel.create). 1 cell = 1 world
  /// unit, so with the production grid this is (x + 0.5, y + 0.5).
  let center (cell: struct (int * int)) (cellSize: Vector2) =
    let struct (x, y) = cell

    Vector2(
      float32 x * cellSize.X + cellSize.X / 2f,
      float32 y * cellSize.Y + cellSize.Y / 2f
    )

// ─────────────────────────────────────────────────────────────
// Semantic input — GameAction + its InputMap live here (not
// Input.fs) because the sim's action consumption (Application.
// handleActions) compiles before the input wiring file; KeyCode
// and InputMap are Mibo.Input core types, backend-neutral.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type GameAction =
  | StartNextWave
  /// Select the tower preset at hotbar slot 0-9 (keys 1-9 and 0).
  | SelectTower of slot: int
  | Restart
  | ResetCamera
  | PanLeft
  | PanRight
  | PanUp
  | PanDown

module Inputs =

  /// The key bindings — the semantic map the InputMapper subscription
  /// evaluates. F3 stays out: the diagnostics overlay is FRONTEND
  /// state, toggled by a direct subscription in Input.fs.
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key StartNextWave KeyCode.Space
    |> InputMap.key StartNextWave KeyCode.Enter
    |> InputMap.key (SelectTower 0) KeyCode.D1
    |> InputMap.key (SelectTower 1) KeyCode.D2
    |> InputMap.key (SelectTower 2) KeyCode.D3
    |> InputMap.key (SelectTower 3) KeyCode.D4
    |> InputMap.key (SelectTower 4) KeyCode.D5
    |> InputMap.key (SelectTower 5) KeyCode.D6
    |> InputMap.key (SelectTower 6) KeyCode.D7
    |> InputMap.key (SelectTower 7) KeyCode.D8
    |> InputMap.key (SelectTower 8) KeyCode.D9
    |> InputMap.key (SelectTower 9) KeyCode.D0
    |> InputMap.key Restart KeyCode.R
    |> InputMap.key ResetCamera KeyCode.Home
    |> InputMap.key PanLeft KeyCode.A
    |> InputMap.key PanLeft KeyCode.Left
    |> InputMap.key PanRight KeyCode.D
    |> InputMap.key PanRight KeyCode.Right
    |> InputMap.key PanUp KeyCode.W
    |> InputMap.key PanUp KeyCode.Up
    |> InputMap.key PanDown KeyCode.S
    |> InputMap.key PanDown KeyCode.Down

  /// The pan direction a pan action contributes. The pressed key moves
  /// the CAMERA (Up → the view pans north); Camera.Pan subtracts its
  /// input (drag semantics: the world follows the cursor), so keyboard
  /// deltas carry the OPPOSITE sign of the drag they mirror.
  let inline panStep(action: GameAction) : Vector2 =
    match action with
    | PanLeft -> Vector2(1f, 0f)
    | PanRight -> Vector2(-1f, 0f)
    | PanUp -> Vector2(0f, -1f)
    | PanDown -> Vector2(0f, 1f)
    | _ -> Vector2.Zero
