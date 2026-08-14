namespace Defli3D.State

// ─────────────────────────────────────────────────────────────
// Enemy layout — the single source of truth for enemy presentation
// geometry, shared by both render backends and the sim's
// projectile-Y homing.
//
// Convention: 1 cell = 1 world unit; ground tiles' top surface is
// at y = 0.2; the hull models are bottom-anchored (min-Y = 0), so
// a model's SizeY is also its rise. enemyScale applies uniformly;
// per-def Scale multiplies on top (the boss is the grunt hull at
// 1.6×).
//
//   hoverY   — the visual resting Y: the tile top for road walkers,
//              flight altitude for fliers. The view's bob is a
//              per-frame oscillation around this anchor.
//   impactY  — the hull's CENTER Y — where shells should detonate.
//              The flight Y-homing drives shells to it (seeded
//              into ProjectileSpawn.TargetY at fire time).
// ─────────────────────────────────────────────────────────────

module EnemyLayout =

  /// The ONE shared visual scale for enemy hulls. 1 = model size on
  /// a 1-unit tile (hulls read ~0.55 units tall against the 0.6
  /// towers). Was duplicated per backend (0.55); the sim's
  /// impact-Y math needs it shared.
  let enemyScale = 0.55f

  /// The enemy's visual resting Y: walkers stand on the tile top
  /// (0.2), fliers cruise at flight altitude (0.8). The view's bob
  /// rides on this anchor.
  let inline hoverY(def: EnemyDef) : float32 =
    if def.Archetype = EnemyArchetype.Flier then 0.8f else 0.2f

  /// The hull's CENTER Y — hover height plus half the scaled hull
  /// height — where shells should detonate. ProjectileSpawn.TargetY
  /// is seeded from this at fire time and the flight Y-homing
  /// drives the shell to it.
  let inline impactY(def: EnemyDef) : float32 =
    hoverY def + def.HullModel.SizeY * def.Scale * enemyScale * 0.5f
