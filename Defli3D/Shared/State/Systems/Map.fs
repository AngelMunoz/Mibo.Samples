namespace Defli3D.State

// ─────────────────────────────────────────────────────────────
// Def stores (EnemyDefs / BossAura / TowerDefs) — the VALUES
// behind the Domain.fs def types. They live here, not in
// Domain.fs, because they bind Models.fs bindings (enemy-ufo-a,
// weapon-ballista, …) and Models.fs compiles after Domain.fs (it
// needs ModelInfo); F# has no cross-file forward references, so
// the first post-Models file hosts them. Same namespace as
// Domain.fs — every consumer sees them exactly as Defli's.
// ─────────────────────────────────────────────────────────────

module EnemyDefs =

  // Rebalance (ballistics rework): base speeds halved so level 1-3
  // DUMBFIRE shots (no seek) still connect; wave speed scaling
  // (+7 %/tier) eventually outruns prediction — misses become a real
  // mechanic late-game.
  let grunt = {
    Key = "grunt"
    Archetype = EnemyArchetype.Grunt
    Hp = 40
    Speed = 0.5f
    Resist = 0f
    GoldReward = 3
    HullModel = Models.enemyUfoA
    WeaponModel = ValueNone
    Scale = 1f
  }

  let runner = {
    Key = "runner"
    Archetype = EnemyArchetype.Runner
    Hp = 20
    Speed = 0.85f
    Resist = 0f
    GoldReward = 5
    HullModel = Models.enemyUfoB
    WeaponModel = ValueNone
    Scale = 1f
  }

  let tank = {
    Key = "tank"
    Archetype = EnemyArchetype.Tank
    Hp = 120
    Speed = 0.28f
    Resist = 0f
    GoldReward = 7
    HullModel = Models.enemyUfoC
    WeaponModel = ValueNone
    Scale = 1f
  }

  /// Flies the straight line spawn → base (ignores the road).
  /// No weapon mount.
  let flier = {
    Key = "flier"
    Archetype = EnemyArchetype.Flier
    Hp = 30
    Speed = 1.0f
    Resist = 0f
    GoldReward = 10
    HullModel = Models.enemyUfoD
    WeaponModel = ValueNone
    Scale = 1f
  }

  /// Boss — every 5th wave's leader (Phase 6). Slow, solid HP pool,
  /// suppresses nearby towers (BossAura), splits into grunts on death.
  /// The grunt hull (ufo-a) rendered at 1.6×.
  let boss = {
    Key = "boss"
    Archetype = EnemyArchetype.Boss
    Hp = 300
    Speed = 0.2f
    Resist = 0f
    GoldReward = 50
    HullModel = Models.enemyUfoA
    WeaponModel = ValueNone
    Scale = 1.6f
  }

  let all = [| grunt; runner; tank; flier; boss |]

/// Boss aura + split parameters (Phase 6) — constants keyed off the
/// Boss ARCHETYPE, not EnemyDef fields: a per-def field would ripple
/// through every def literal and fixture for one archetype's sake.
module BossAura =

  /// Towers within this distance (world units — 2 cells) of a live
  /// boss have their fire rate multiplied by Factor.
  let Radius = 2f

  /// Fire-rate multiplier for suppressed towers (0.5 = halved).
  let Factor = 0.5f

  /// Visual radius (world units) of the boss body aura — a fresnel shell
  /// centered on the hull, slightly larger than the boss body so it reads
  /// as a glow surrounding it. Gameplay suppression still uses Radius.
  let VisualRadius = 1.3f

  /// Grunts spawned at the corpse when a boss dies.
  let SplitCount = 3

  /// The child def (the wave's tier scale is applied by Application).
  let SplitInto = EnemyDefs.grunt

module TowerDefs =

  // ── Shared zone presets (catapult > cannon > none) ──────────
  // Zones slow AND tick damage; stacking is capped per enemy by
  // MaxStacks concurrent zones (design: 5).
  let private zoneCatapult = {
    Radius = 1.3f
    Seconds = 4f
    Slow = 0.6f
    TickDamage = 4
    TickInterval = 0.5f
    MaxStacks = 5
    Affects = TargetDomain.Ground
  }

  let private zoneCannon = {
    Radius = 0.9f
    Seconds = 2.5f
    Slow = 0.8f
    TickDamage = 3
    TickInterval = 0.5f
    MaxStacks = 5
    Affects = TargetDomain.Ground
  }

  let private zoneArrow = {
    Radius = 0.6f
    Seconds = 1.5f
    Slow = 0.85f
    TickDamage = 0
    TickInterval = 0.5f
    MaxStacks = 5
    Affects = TargetDomain.Any
  }

  let private noZone = ValueNone

  let private zone z = ValueSome z

  // ── The 1-0 preset table (one chassis × weapon bundle per key) ──

  /// 1 — Ballista emplacement: the cheap starter. A real gun on a
  /// round pad; no structural levels.
  let sentry = {
    Key = "sentry"
    Name = "Sentry"
    Chassis = Chassis.Emplacement
    Cost = 40
    Range = 3
    Damage = 20
    FireRate = 0.7f
    RatePerLevel = 0.1f
    ProjectileSpeed = 7f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
    ImpactRadius = 0.25f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = noZone
    WeaponModel = ValueSome Models.weaponBallista
    GunScale = 1f
    ProjectileModel = Models.ammoArrow
    ProjectileScale = 0.7f
    MuzzleDust = true
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.Weakest
    UpgradeCost = 30
    MaxLevel = 5
  }

  /// 2 — Turret emplacement: a musket that becomes a machine gun —
  /// slow single shots at level 1, the steepest fire-rate curve of the
  /// set (bullets seek from level 4).
  let gunpost = {
    Key = "gunpost"
    Name = "Gun Post"
    Chassis = Chassis.Emplacement
    Cost = 60
    Range = 3
    Damage = 25
    FireRate = 0.8f
    RatePerLevel = 0.9f
    ProjectileSpeed = 8f
    ProjectileSpeedScales = true
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
    ImpactRadius = 0.25f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = noZone
    WeaponModel = ValueSome Models.weaponTurret
    GunScale = 1f
    ProjectileModel = Models.ammoBullet
    ProjectileScale = 0.7f
    MuzzleDust = false
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.First
    UpgradeCost = 45
    MaxLevel = 5
  }

  /// 3 — Cannon emplacement: the cannon on a wooden mount; semi-arced
  /// shells with a medium slow+DoT zone.
  let cannonPost = {
    Key = "cannonpost"
    Name = "Cannon Post"
    Chassis = Chassis.Emplacement
    Cost = 90
    Range = 3
    Damage = 25
    FireRate = 0.5f
    RatePerLevel = 0.1f
    ProjectileSpeed = 5f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.SemiArc
    ImpactRadius = 0.8f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = zone zoneCannon
    WeaponModel = ValueSome Models.weaponCannon
    GunScale = 1f
    ProjectileModel = Models.ammoCannonball
    ProjectileScale = 1f
    MuzzleDust = false
    Targets = TargetDomain.Ground
    TargetPolicy = TargetPolicy.Weakest
    UpgradeCost = 70
    MaxLevel = 5
  }

  /// 4 — Catapult emplacement: the catapult on a wooden mount; slow
  /// parabolic boulders, the biggest lasting zone.
  let catapultPost = {
    Key = "catapultpost"
    Name = "Catapult Post"
    Chassis = Chassis.Emplacement
    Cost = 120
    Range = 4
    Damage = 35
    FireRate = 0.3f
    RatePerLevel = 0.1f
    ProjectileSpeed = 3.5f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Arc
    ImpactRadius = 1.2f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = zone zoneCatapult
    WeaponModel = ValueSome Models.weaponCatapult
    GunScale = 1f
    ProjectileModel = Models.ammoBoulder
    ProjectileScale = 1f
    MuzzleDust = false
    Targets = TargetDomain.Ground
    TargetPolicy = TargetPolicy.First
    UpgradeCost = 90
    MaxLevel = 5
  }

  /// 5 — Arrow deck (round modular, middle-a): a FAN of small arrows
  /// around the predicted point; each hit leaves a small slow patch.
  let arrowDeck = {
    Key = "arrowdeck"
    Name = "Arrow Deck"
    Chassis = Chassis.Deck 0
    Cost = 70
    Range = 3
    Damage = 12
    FireRate = 0.5f
    RatePerLevel = 0.1f
    ProjectileSpeed = 6f
    ProjectileSpeedScales = false
    Volley = 4
    Spread = 0.6f
    Trajectory = Trajectory.Flat
    ImpactRadius = 0.35f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = zone zoneArrow
    WeaponModel = ValueNone
    GunScale = 1f
    ProjectileModel = Models.ammoArrow
    ProjectileScale = 0.35f
    MuzzleDust = true
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.First
    UpgradeCost = 55
    MaxLevel = 5
  }

  /// 6 — Bullet deck (middle-c): bursts of small bullets, no
  /// residue — pure DPS from a static four-opening deck.
  let bulletDeck = {
    Key = "bulletdeck"
    Name = "Bullet Deck"
    Chassis = Chassis.Deck 2
    Cost = 90
    Range = 3
    Damage = 14
    FireRate = 0.8f
    RatePerLevel = 0.45f
    ProjectileSpeed = 8f
    ProjectileSpeedScales = true
    Volley = 3
    Spread = 0.4f
    Trajectory = Trajectory.Flat
    ImpactRadius = 0.3f
    Piercing = false
    Homing = HomingPolicy.FromLevel 4
    Zone = noZone
    WeaponModel = ValueNone
    GunScale = 1f
    ProjectileModel = Models.ammoBullet
    ProjectileScale = 0.35f
    MuzzleDust = false
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.Closest
    UpgradeCost = 70
    MaxLevel = 5
  }

  /// 7 — Cannon bunker (square): the splash wall. Cannon enclosed in
  /// the bay, bigger impact + zone than the deck variant.
  let bunker = {
    Key = "bunker"
    Name = "Bunker"
    Chassis = Chassis.Bunker
    Cost = 130
    Range = 3
    Damage = 28
    FireRate = 0.4f
    RatePerLevel = 0.1f
    ProjectileSpeed = 5f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.SemiArc
    ImpactRadius = 1.1f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = zone zoneCannon
    WeaponModel = ValueSome Models.weaponCannon
    GunScale = 1f
    ProjectileModel = Models.ammoCannonball
    ProjectileScale = 1f
    MuzzleDust = false
    Targets = TargetDomain.Ground
    TargetPolicy = TargetPolicy.Strongest
    UpgradeCost = 100
    MaxLevel = 5
  }

  /// 8 — Catapult battery (base + open top): slow boulder, full
  /// parabola, the biggest lasting zone. The crowd-control answer.
  let catapult = {
    Key = "catapult"
    Name = "Catapult"
    Chassis = Chassis.Battery
    Cost = 160
    Range = 4
    Damage = 40
    FireRate = 0.3f
    RatePerLevel = 0.1f
    ProjectileSpeed = 3.5f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Arc
    ImpactRadius = 1.3f
    Piercing = false
    Homing = HomingPolicy.Never
    Zone = zone zoneCatapult
    WeaponModel = ValueSome Models.weaponCatapult
    GunScale = 1f
    ProjectileModel = Models.ammoBoulder
    ProjectileScale = 1f
    MuzzleDust = false
    Targets = TargetDomain.Ground
    TargetPolicy = TargetPolicy.First
    UpgradeCost = 120
    MaxLevel = 5
  }

  /// 9 — Piercer battery: the large ballista. A fast large arrow
  /// that flies THROUGH the lane, damaging every enemy it passes.
  let piercer = {
    Key = "piercer"
    Name = "Piercer"
    Chassis = Chassis.Battery
    Cost = 200
    Range = 5
    Damage = 35
    FireRate = 0.5f
    RatePerLevel = 0.1f
    ProjectileSpeed = 10f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
    ImpactRadius = 0.4f
    Piercing = true
    Homing = HomingPolicy.Never
    Zone = noZone
    WeaponModel = ValueSome Models.weaponBallista
    GunScale = 1.7f
    ProjectileModel = Models.ammoArrow
    ProjectileScale = 1.4f
    MuzzleDust = true
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.Strongest
    UpgradeCost = 150
    MaxLevel = 5
  }

  /// 0 — Rocket pad (heavy emplacement): the large turret. Rockets
  /// always seek from level 1 and explode on arrival.
  let rocketPad = {
    Key = "rocketpad"
    Name = "Rocket Pad"
    Chassis = Chassis.Emplacement
    Cost = 180
    Range = 4
    Damage = 30
    FireRate = 0.8f
    RatePerLevel = 0.1f
    ProjectileSpeed = 6f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
    ImpactRadius = 1.0f
    Piercing = false
    Homing = HomingPolicy.Always
    Zone = noZone
    WeaponModel = ValueSome Models.weaponTurret
    GunScale = 1.7f
    ProjectileModel = Models.ammoBullet
    ProjectileScale = 1.6f
    MuzzleDust = false
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.First
    UpgradeCost = 140
    MaxLevel = 5
  }

  /// The 1-0 hotbar order (index 0 = key 1, …, index 9 = key 0):
  /// the four basic guns on emplacements first (1-4), then the
  /// decks, the bunker and the heavies.
  let slots = [|
    sentry
    gunpost
    cannonPost
    catapultPost
    arrowDeck
    bulletDeck
    bunker
    catapult
    piercer
    rocketPad
  |]

  let all = slots

  /// The upgrade formula (pure): +25 % damage, +def.RatePerLevel fire
  /// rate (guns scale steeply, loaders near-flat), +0.5 range per level
  /// over the base def. Homing stays a per-def policy (see HomingPolicy)
  /// — level NEVER unlocks seeking by itself. Level 1 = the base def.
  let effectiveDef (def: TowerDef) (level: int) : TowerDef =
    if level <= 1 then
      def
    else
      let l = float32(level - 1)

      {
        def with
            Damage = int(float def.Damage * (1.0 + 0.25 * float l))
            FireRate = def.FireRate * (1f + def.RatePerLevel * l)
            Range = def.Range + int(l * 0.5f)
      }

namespace Defli3D.State.Systems

open System.Numerics
open Mibo.Layout
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Map sub-system — owns a LayeredGrid2D<MapTile> (one parallel
// CellGrid2D per concern) and the path. Static content (built once
// at state init, never mutated — same rule as Kimo's map/stores;
// NOT adaptive).
//
// Layers (MapLayers):
//   Terrain    — grass fill (visual base)
//   Path       — the road, stamped over the waypoint segments
//   Buildable  — build permission; the road stamp overwrites the
//                cells under it with the non-buildable path tile
//   Waypoints  — the path's vertex cells (spawn/base markers)
//
// The road is carved with the stamp machinery (Layout.fill /
// repeatX / repeatY over GridSection2D), never hand-rolled loops.
//
// 1 cell = 1 world unit (Defli's 64 px ÷ 64): cellSize is
// Vector2(1, 1) and Path holds cell-center waypoints at 0.5
// offsets. The MapModel stores LOGICAL data only; the visual
// content selection (road piece + rotation, terrain and decoration
// models) lives in MapModel.cellPieces — the single source of truth
// consumed by both backends' map bakes.
// ─────────────────────────────────────────────────────────────

/// Layer indices of the map's parallel grids.
[<RequireQualifiedAccess>]
module MapLayers =
  [<Literal>]
  let Terrain = 0

  [<Literal>]
  let Path = 1

  [<Literal>]
  let Buildable = 2

  [<Literal>]
  let Waypoints = 3

  [<Literal>]
  let Decorations = 4

type MapModel = {
  Grid: LayeredGrid2D<MapTile>
  /// World-space waypoint centers (spawn → base) — the movement
  /// (physics) phase walks these.
  Path: Vector2[]
  SpawnCell: struct (int * int)
  BaseCell: struct (int * int)
}

module MapModel =

  /// 1 cell = 1 world unit — the grid's uniform cell size.
  let private cellSize = Vector2(1f, 1f)

  let private grassTile = {
    Terrain = TerrainKind.Grass
    IsPath = false
    Buildable = true
    IsWaypoint = false
    Decoration = ValueNone
  }

  let private pathTile = {
    Terrain = TerrainKind.Dirt
    IsPath = true
    Buildable = false
    IsWaypoint = false
    Decoration = ValueNone
  }

  let private nonBuildableTile = { grassTile with Buildable = false }

  /// A decorations-layer row: the model to draw over the terrain
  /// (dirt blends keep Buildable = true — ground dressing; props on
  /// procedural maps set Buildable = false — obstacles).
  let inline private decoTile(model: ModelInfo) = {
    grassTile with
        Decoration = ValueSome model
  }

  let inline private obstacleTile(model: ModelInfo) = {
    decoTile model with
        Buildable = false
  }

  /// A layer's CellGrid2D (all layers exist after create).
  let inline layer (index: int) (m: MapModel) : CellGrid2D<MapTile> =
    let struct (grid, _) = LayeredGrid2D.getOrAddLayer index m.Grid
    grid

  let inline terrain(m: MapModel) = layer MapLayers.Terrain m
  let inline pathGrid(m: MapModel) = layer MapLayers.Path m
  let inline buildableGrid(m: MapModel) = layer MapLayers.Buildable m
  let inline waypoints(m: MapModel) = layer MapLayers.Waypoints m
  let inline decorations(m: MapModel) = layer MapLayers.Decorations m

  /// A cell is buildable iff its Buildable layer row carries Buildable
  /// (the road stamp overwrote the cells under it).
  let inline isBuildable (x: int) (y: int) (m: MapModel) : bool =
    m |> buildableGrid |> CellGrid2D.get x y |> ValueOption.exists _.Buildable

  /// Hand-authored Level-1 path, in cells (spawn left → base right).
  let private waypointCells = [|
    struct (0, 4)
    struct (7, 4)
    struct (7, 8)
    struct (14, 8)
    struct (14, 2)
    struct (19, 2)
  |]

  /// One axis-aligned road segment as a stamp (repeatX for horizontal,
  /// repeatY for vertical — inclusive of both endpoints).
  let inline private stampSegment
    (struct (px, py): struct (int * int))
    (struct (tx, ty): struct (int * int))
    (section: GridSection2D<MapTile>)
    : GridSection2D<MapTile> =
    if py = ty then
      Layout.repeatX (min px tx) py (abs(tx - px) + 1) pathTile section
    else
      Layout.repeatY px (min py ty) (abs(ty - py) + 1) pathTile section

  /// The whole road as one stamp chain (all waypoint segments).
  let inline private stampPath
    (section: GridSection2D<MapTile>)
    : GridSection2D<MapTile> =
    let mutable acc = section

    for i in 1 .. waypointCells.Length - 1 do
      acc <- stampSegment waypointCells[i - 1] waypointCells[i] acc

    acc

  // ── Level-2 procedural generation ──

  /// Prop model picked deterministically from the placement cell — no
  /// extra RNG stream (Kimo's rule: RNG streams are owned, never shared).
  let inline private propFor (x: int) (y: int) : ModelInfo =
    Models.decorations[(x * 7 + y * 13) % Models.decorations.Length]

  /// Deterministic roll in [0, 1) from a cell + salt (same rule).
  let inline private hashRoll (x: int) (y: int) (salt: int) : float =
    float((x * 31 + y * 17 + salt * 7) % 997) / 997.0

  /// Scatter props as OBSTACLES: the Decorations layer gets the prop
  /// row, the Buildable layer is cleared under it. The stamp's section
  /// offset IS the placement cell — prop variety derives from it.
  let private scatterObstacles
    (count: int)
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (buildable: CellGrid2D<MapTile>)
    : unit =
    Layout.scatterStamp
      count
      seed
      (fun s ->
        let gx = s.OffsetX
        let gy = s.OffsetY
        let model = propFor gx gy
        CellGrid2D.set gx gy (obstacleTile model) deco
        CellGrid2D.set gx gy nonBuildableTile buildable
        s)
      (createSection deco)
    |> ignore

  /// Visual-only props (HandAuthored): decoration rows, never on the
  /// road, buildability untouched.
  let private scatterVisualProps
    (count: int)
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (pathLayer: CellGrid2D<MapTile>)
    : unit =
    Layout.scatterStamp
      count
      seed
      (fun s ->
        let gx = s.OffsetX
        let gy = s.OffsetY

        let onPath =
          pathLayer |> CellGrid2D.get gx gy |> ValueOption.exists _.IsPath

        if onPath then
          s
        else
          CellGrid2D.set gx gy (decoTile(propFor gx gy)) deco
          s)
      (createSection deco)
    |> ignore

  /// Dirt details hugging the road — ONE coherent family (detail-dirt
  /// / detail-dirt-large), deterministic per cell. Props keep their
  /// spot (blends only fill empty decoration cells). Unlike the 2D
  /// version there is no orientation-dependent frame: the view may
  /// Y-rotate the model freely.
  let private scatterBlends
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (pathLayer: CellGrid2D<MapTile>)
    : unit =
    CellGrid2D.iter
      (fun x y tile ->
        if tile.IsPath then
          for struct (nx, ny) in Grid2DSpatial.neighbors4 x y pathLayer do
            // The Path layer is sparse: an absent cell is grass (free);
            // only a PRESENT path-marked cell is the road.
            let freeGrass =
              match pathLayer |> CellGrid2D.get nx ny with
              | ValueSome t -> not t.IsPath
              | ValueNone -> true

            let empty = (deco |> CellGrid2D.get nx ny).IsNone

            if freeGrass && empty && hashRoll nx ny seed < 0.45 then
              let r = hashRoll nx ny (seed + 1)

              let model =
                if r < 0.3 then
                  Models.detailDirtLarge
                else
                  Models.detailDirt

              CellGrid2D.set nx ny (decoTile model) deco)
      pathLayer

  /// One procedural attempt on a FRESH grid: obstacles scattered with
  /// the given seed, the road carved by findPath around them, and a
  /// floodFill reachability validation (independent of A*).
  let private tryProcedural
    (cfg: WorldConfig)
    (seed: int)
    : struct (LayeredGrid2D<MapTile> *
      struct (int * int)[] *
      struct (int * int) *
      struct (int * int)) voption
    =
    let grid =
      LayeredGrid2D.create cfg.GridCols cfg.GridRows cellSize Vector2.Zero
      |> LayeredLayout.layer MapLayers.Terrain (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredLayout.layer MapLayers.Buildable (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)

    let struct (deco, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Decorations grid

    let struct (buildable, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Buildable grid

    let struct (terrain, _) = LayeredGrid2D.getOrAddLayer MapLayers.Terrain grid

    let obstacleCount = cfg.GridCols * cfg.GridRows / 10
    scatterObstacles obstacleCount seed deco buildable

    let rng = System.Random(seed)
    let spawnY = rng.Next(1, cfg.GridRows - 1)
    let baseY = rng.Next(1, cfg.GridRows - 1)

    let isPassable x y =
      match deco |> CellGrid2D.get x y with
      | ValueSome t -> t.Buildable
      | ValueNone -> true

    match
      Grid2DSpatial.findPath
        0
        spawnY
        (cfg.GridCols - 1)
        baseY
        isPassable
        (fun _ _ _ _ -> 1f)
        terrain
    with
    | ValueNone -> ValueNone
    | ValueSome pathCells ->
      // floodFill validation: the base must be reachable from spawn
      // over non-obstacle cells (independent of the A* result).
      let reachable = Grid2DSpatial.floodFill 0 spawnY isPassable terrain

      let baseReachable =
        reachable
        |> Array.exists(fun struct (x, y) ->
          struct (x, y) = struct (cfg.GridCols - 1, baseY))

      if not baseReachable then
        ValueNone
      else
        // Carve the road along the found path (stamp machinery — the
        // path is 4-adjacent, so each pair is one repeatX/repeatY).
        let struct (pathLayer, _) =
          LayeredGrid2D.getOrAddLayer MapLayers.Path grid

        for i in 1 .. pathCells.Length - 1 do
          stampSegment pathCells[i - 1] pathCells[i] (createSection pathLayer)
          |> ignore

          stampSegment pathCells[i - 1] pathCells[i] (createSection buildable)
          |> ignore

        // Waypoints: every path cell is a waypoint (spawn/base markers
        // ride on the same layer — the view keys the base mount on
        // BaseCell).
        let waypointTile = { grassTile with IsWaypoint = true }

        grid
        |> LayeredLayout.layer MapLayers.Waypoints (fun s ->
          pathCells
          |> Array.fold
            (fun acc struct (x, y) -> Layout.set x y waypointTile acc)
            s)
        |> ignore

        ValueSome(
          grid,
          pathCells,
          struct (0, spawnY),
          struct (cfg.GridCols - 1, baseY)
        )

  /// Shared tail: world-space path centers + the blend pass.
  let private buildModel
    (seed: int)
    (grid: LayeredGrid2D<MapTile>)
    (pathCells: struct (int * int)[])
    (spawn: struct (int * int))
    (baseCell: struct (int * int))
    : MapModel =
    let struct (terrainLayer, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Terrain grid

    let struct (pathLayer, _) = LayeredGrid2D.getOrAddLayer MapLayers.Path grid

    let struct (deco, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Decorations grid

    scatterBlends seed deco pathLayer

    let path =
      pathCells
      |> Array.map(fun struct (x, y) ->
        let topLeft = CellGrid2D.getWorldPos x y terrainLayer
        topLeft + cellSize / 2f)

    {
      Grid = grid
      Path = path
      SpawnCell = spawn
      BaseCell = baseCell
    }

  /// Level-1: the fixed hand-authored road + visual-only props.
  let private handAuthored(cfg: WorldConfig) : MapModel =
    let grid =
      LayeredGrid2D.create cfg.GridCols cfg.GridRows cellSize Vector2.Zero
      |> LayeredLayout.layer MapLayers.Terrain (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredLayout.layer MapLayers.Buildable (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredLayout.layer MapLayers.Path stampPath
      |> LayeredLayout.layer MapLayers.Buildable stampPath

    let waypointTile = { grassTile with IsWaypoint = true }

    grid
    |> LayeredLayout.layer MapLayers.Waypoints (fun s ->
      waypointCells
      |> Array.fold
        (fun acc struct (x, y) -> Layout.set x y waypointTile acc)
        s)
    |> ignore

    let struct (deco, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Decorations grid

    let struct (pathLayer, _) = LayeredGrid2D.getOrAddLayer MapLayers.Path grid

    scatterVisualProps (cfg.GridCols * cfg.GridRows / 8) cfg.Seed deco pathLayer

    buildModel
      cfg.Seed
      grid
      waypointCells
      waypointCells[0]
      waypointCells[waypointCells.Length - 1]

  /// Level-2: seeded obstacle scatter → findPath road → floodFill
  /// validation. Seeds advance until a valid layout lands; after 16
  /// attempts it falls back to the hand-authored road (guaranteed
  /// valid — the game never boots to a broken map).
  let private procedural(cfg: WorldConfig) : MapModel =
    let rec attempt (seed: int) (left: int) : MapModel =
      if left = 0 then
        handAuthored cfg
      else
        match tryProcedural cfg seed with
        | ValueSome struct (grid, pathCells, spawn, baseCell) ->
          buildModel cfg.Seed grid pathCells spawn baseCell
        | ValueNone -> attempt (seed + 1) (left - 1)

    attempt cfg.Seed 16

  let create(cfg: WorldConfig) : MapModel =
    match cfg.MapVariant with
    | MapVariant.HandAuthored -> handAuthored cfg
    | MapVariant.Procedural -> procedural cfg

  // ── Visual content selection ─────────────────────────────────
  // The single source of truth for the map's rendered content: road
  // piece / terrain / decoration model + Y rotation + Y offset for
  // every cell. Both backends' bakes consume these and add only the
  // grid→CellGrid3D + native matrix conversion.

  /// Deterministic Y rotation for visual variety (no RNG).
  let inline varietyRotation (x: int) (y: int) : float32 =
    float32((x * 7 + y * 13) % 4) * System.MathF.PI / 2f

  /// Terrain model for a TerrainKind (the map only bakes Grass rows
  /// today — the other kinds serve future map variants).
  let inline terrainModel(kind: TerrainKind) : ModelInfo =
    match kind with
    | TerrainKind.Grass -> Models.tileGrass
    | TerrainKind.Dirt -> Models.tileDirt
    | TerrainKind.Stone -> Models.tileRock
    | TerrainKind.Sand -> Models.tileBump

  /// The road piece for a path cell from its path neighbors, plus the
  /// Y rotation that aligns its openings with the road's continuation.
  ///
  /// Rotation convention — measured from the kit's GLB meshes (vertex
  /// analysis of the road-surface crossings at the footprint edges,
  /// rotation 0):
  ///   tile-straight     — the road runs N–S
  ///   tile-end-round    — the road opening faces S (+Z)
  ///   tile-corner-round — the road exits E and S (+X, +Z)
  ///   tile-split        — the closed side faces N (−Z)
  /// Positive rotation is clockwise when viewed from above (+Y).
  /// The kit's corner piece has only ONE hand (E/S at 0°, rotating to
  /// N/E, W/N, S/W) — a counter-clockwise corner (e.g. W/S on the
  /// hand-authored road) is not representable and reads as the nearest
  /// rotation until a mirrored piece exists.
  let inline roadPiece
    (path: CellGrid2D<MapTile>)
    (x: int)
    (y: int)
    : struct (ModelInfo * float32) =
    let isPath gx gy =
      path |> CellGrid2D.get gx gy |> ValueOption.exists(fun t -> t.IsPath)

    let n = isPath x (y - 1)
    let s = isPath x (y + 1)
    let e = isPath (x + 1) y
    let w = isPath (x - 1) y

    let count =
      (if n then 1 else 0)
      + (if s then 1 else 0)
      + (if e then 1 else 0)
      + if w then 1 else 0

    match count with
    | 4 -> struct (Models.roadCrossing, 0f)
    | 3 ->
      // Split — the closed side (N at 0°) faces the missing neighbor.
      if not n then
        struct (Models.roadSplit, 0f)
      elif not e then
        struct (Models.roadSplit, System.MathF.PI * 1.5f)
      elif not s then
        struct (Models.roadSplit, System.MathF.PI)
      else
        struct (Models.roadSplit, System.MathF.PI / 2f)
    | 2 when n && s -> struct (Models.roadStraight, 0f)
    | 2 when e && w -> struct (Models.roadStraight, System.MathF.PI / 2f)
    | 2 when n && e -> struct (Models.roadCornerRound, System.MathF.PI / 2f)
    | 2 when e && s -> struct (Models.roadCornerRound, 0f)
    | 2 when s && w -> struct (Models.roadCornerRound, System.MathF.PI * 1.5f)
    | 2 when w && n -> struct (Models.roadCornerRound, System.MathF.PI)
    | 1 when n -> struct (Models.roadEndRound, System.MathF.PI)
    | 1 when e -> struct (Models.roadEndRound, System.MathF.PI / 2f)
    | 1 when s -> struct (Models.roadEndRound, 0f)
    | 1 when w -> struct (Models.roadEndRound, System.MathF.PI * 1.5f)
    | _ -> struct (Models.roadStraight, 0f)

  /// One rendered piece for a map cell: the content model + Y rotation
  /// in radians + Y offset. The Y offset is 0 for ground pieces — the
  /// kit's models are bottom-anchored (the mesh base sits at y = 0,
  /// measured from the GLBs) — and exists for future map variants.
  [<Struct>]
  type CellPiece = {
    Model: ModelInfo
    Rotation: float32
    YOffset: float32
  }

  /// The full visual selection for one map cell: the GROUND piece —
  /// spawn/base cells render their marker tiles, path cells get the
  /// neighbor-picked road piece, other cells the terrain tile
  /// (rotation 0 — the shared colormap texture must stay aligned
  /// across adjacent tiles) — plus the optional DECORATION piece
  /// rendered one layer ABOVE the ground (the tile top is y = 0.2 and
  /// the kit's decorations are bottom-anchored, so their Y offset is
  /// 0.2; props are variety-rotated).
  let inline cellPieces
    (map: MapModel)
    (x: int)
    (y: int)
    : struct (CellPiece * CellPiece voption) =
    let path = pathGrid map

    let ground =
      if
        path |> CellGrid2D.get x y |> ValueOption.exists(fun t -> t.IsPath)
      then
        if struct (x, y) = map.SpawnCell then
          {
            Model = Models.tileSpawn
            Rotation = 0f
            YOffset = 0f
          }
        elif struct (x, y) = map.BaseCell then
          {
            Model = Models.tileSpawnEnd
            Rotation = 0f
            YOffset = 0f
          }
        else
          let struct (model, rot) = roadPiece path x y

          {
            Model = model
            Rotation = rot
            YOffset = 0f
          }
      else
        match terrain map |> CellGrid2D.get x y with
        | ValueSome tile -> {
            Model = terrainModel tile.Terrain
            Rotation = 0f
            YOffset = 0f
          }
        | ValueNone ->
            {
              Model = Models.tileGrass
              Rotation = 0f
              YOffset = 0f
            }

    let decoration =
      decorations map
      |> CellGrid2D.get x y
      |> ValueOption.bind(fun t -> t.Decoration)
      |> ValueOption.map(fun deco -> {
        Model = deco
        Rotation = varietyRotation x y
        YOffset = 0.2f
      })

    struct (ground, decoration)
