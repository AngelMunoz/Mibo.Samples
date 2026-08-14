namespace Defli3D.State

open System.Numerics

// ─────────────────────────────────────────────────────────────
// Typed IDs (units of measure — zero-cost, struct-friendly)
// ─────────────────────────────────────────────────────────────

[<Measure>]
type EnemyId

[<Measure>]
type TowerId

[<Measure>]
type ProjectileId

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
    StartingGold = 60
    StartingLives = 20
    WaveClearBonus = 25
    GridCols = 20
    GridRows = 12
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

[<Struct>]
type EnemyDef = {
  Key: string
  Archetype: EnemyArchetype
  Hp: int
  /// World units per second (1 cell = 1 unit; Defli's px/s ÷ 64).
  Speed: float32
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

/// Join row of the EnemyViews projection (Positions × Healths × Motions).
/// Positions are logical XZ-plane coordinates (x → x, y → z at the
/// render edge).
[<Struct>]
type EnemyView = {
  Pos: Vector2
  Hp: int
  MaxHp: int
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

[<Struct>]
type TowerDef = {
  Key: string
  Name: string
  Cost: int
  /// Range in grid cells — 1 cell = 1 world unit, so this is also the
  /// world-space range (Chebyshev ring narrowed by exact distance).
  Range: int
  Damage: int
  /// Shots per second.
  FireRate: float32
  /// World units per second (Defli's px/s ÷ 64).
  ProjectileSpeed: float32
  /// Weapon model mounted on the tower top (aimed at the target by
  /// the view). The tower BODY (stacked round/square parts, picked by
  /// Key/level) is a view concern.
  WeaponModel: ModelInfo
  /// Projectile model (the shell/arrow/bullet in flight).
  ProjectileModel: ModelInfo
  TargetPolicy: TargetPolicy
  /// Slow applied by this tower's projectiles: 1 = no slow, < 1 = the
  /// enemy's speed factor for SlowSeconds (frost tower).
  SlowFactor: float32
  SlowSeconds: float32
  /// Blast radius in world units (cannon): every enemy within this
  /// distance of the impact point takes full Damage. 0 = single-target.
  SplashRadius: float32
  /// Gold cost per upgrade level (flat) and the level cap.
  UpgradeCost: int
  MaxLevel: int
}

/// Per-tower components (rows in the Towers sub-system's CMaps).
/// Static vs runtime is the write-frequency grouping: Statics is written
/// once (placement), Runtimes every tick (cooldown/target).
[<Struct>]
type TowerStatic = {
  Def: TowerDef
  Cell: struct (int * int)
}

[<Struct>]
type TowerRuntime = {
  Cooldown: float32
  Target: int<EnemyId> voption
}

// ─────────────────────────────────────────────────────────────
// Projectiles
// ─────────────────────────────────────────────────────────────

/// One in-flight shot (a row in Projectiles.Rows).
[<Struct>]
type ProjectileRow = {
  Pos: Vector2
  /// The shot's CURRENT flight height (seeded from the spawn's
  /// muzzle Y). The sim integrates it toward TargetY in lockstep
  /// with the XZ seek, so the shell arrives at the target's hull
  /// center when the seek arrives.
  Y: float32
  /// The target hull's center Y at fire time (EnemyLayout.impactY)
  /// — the Y-homing destination. Frozen at spawn: no mid-flight
  /// re-lookups.
  TargetY: float32
  TargetEnemy: int<EnemyId>
  /// The target's last recorded position. Live-tracked while the
  /// target is alive; when the target despawns mid-flight the shot
  /// flies on to THIS point and detonates there (no mid-air pop —
  /// splash shells still blast the pack around the corpse).
  LastTargetPos: Vector2
  Damage: int
  /// World units per second.
  Speed: float32
  Lifetime: float32
  /// Slow applied on impact (1 = none; copied from the TowerDef).
  SlowFactor: float32
  SlowSeconds: float32
  /// Blast radius in world units (0 = single-target; from the def).
  SplashRadius: float32
  /// Shell model (from the def).
  ProjectileModel: ModelInfo
}

/// Spawn intent for a projectile — the shot's definitional content
/// (the row minus the id and the lifetime, both system-owned: the id
/// is assigned at spawn, the lifetime is a fixed constant).
[<Struct>]
type ProjectileSpawn = {
  Pos: Vector2
  /// World-space Y of the muzzle at fire time — the shot's flight
  /// origin (TowerLayout.muzzleY via TowerShot.Height).
  Height: float32
  /// The target hull's center Y at fire time (EnemyLayout.impactY)
  /// — the Y-homing destination the flight integrates toward.
  TargetY: float32
  TargetEnemy: int<EnemyId>
  /// Seeded by Application from the target's live position at fire time.
  LastTargetPos: Vector2
  Damage: int
  Speed: float32
  SlowFactor: float32
  SlowSeconds: float32
  SplashRadius: float32
  ProjectileModel: ModelInfo
}

/// Difficulty scaling per wave tier (every 5 waves the enemies get
/// harder — Phase 5 difficulty curve data).
[<Struct>]
type WaveScale = {
  Hp: float32
  Speed: float32
  Reward: float32
}

module WaveScale =

  /// Tier = the wave's difficulty bracket: waves 1-4 → tier 0,
  /// waves 5-9 → tier 1, etc. Each tier: +60 % HP, +7 % speed,
  /// +20 % reward.
  let ofWave(number: int) : WaveScale =
    let tier = max 0 (number / 5)
    let hp = float32(1.6 ** float tier)
    let speed = float32(1.07 ** float tier)
    let reward = float32(1.2 ** float tier)

    {
      Hp = hp
      Speed = speed
      Reward = reward
    }

  let inline apply (scale: WaveScale) (def: EnemyDef) : EnemyDef = {
    def with
        Hp = max 1 (int(float def.Hp * float scale.Hp))
        Speed = def.Speed * scale.Speed
        GoldReward = max 1 (int(float def.GoldReward * float scale.Reward))
  }

/// A tower shot (TowerEvent.Fired payload) — what left the barrel.
[<Struct>]
type TowerShot = {
  Tower: int<TowerId>
  Enemy: int<EnemyId>
  Damage: int
  SlowFactor: float32
  SlowSeconds: float32
  SplashRadius: float32
  ProjectileModel: ModelInfo
  /// World-space Y of the muzzle at fire time (presentation payload
  /// — the sim integrates XZ only; TowerLayout.muzzleY).
  Height: float32
}

/// A projectile impact (ProjectileEvent.Impact payload) — what hit.
/// Pos is the detonation point; Y is the flight height at detonation
/// (the flight homes on the target's hull center, so bursts spawn up
/// ON the hull, not at ground level). On a splash hit Application
/// fans out one ApplyDamage per enemy within SplashRadius of it.
[<Struct>]
type ProjectileImpact = {
  Projectile: int<ProjectileId>
  Enemy: int<EnemyId>
  Damage: int
  Pos: Vector2
  /// The flight height at detonation (the shell's Y when the XZ seek
  /// arrived — spawn Y + however much of the Y-homing it covered).
  Y: float32
  SlowFactor: float32
  SlowSeconds: float32
  SplashRadius: float32
}

/// A slow application (EnemyMsg.ApplySlow payload) — factor + expiry.
[<Struct>]
type SlowApply = {
  Enemy: int<EnemyId>
  Factor: float32
  Seconds: float32
}

/// Render row of the state-owned Homing projection
/// (Projectiles.Rows × Enemies.Positions). TargetPos is the target's
/// live position while it lives, the row's LastTargetPos after.
/// Y is the shot's current flight height and TargetY the hull-center
/// destination it homes on — the sim now integrates Y alongside the
/// XZ seek (3D homing toward the hull center), so the shell visibly
/// descends/rises onto the target instead of flying flat at muzzle
/// height.
[<Struct>]
type HomingView = {
  Pos: Vector2
  Y: float32
  TargetY: float32
  TargetPos: Vector2
  Model: ModelInfo
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
