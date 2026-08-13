namespace Defli3D.MonoGame

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// TowersView — tower bodies and weapons from the frame's
// TowerStatics/TowerLevels snapshots. The body is the level's
// complete tower model (tower-round-build-A..F for arrow/frost,
// tower-square-build-A..F for cannon — level 1 → A, 5 → E, 6+ → F),
// the weapon sits on the tower top with an idle slow spin.
//
// Note on aiming: the frame carries no tower runtime (TowerRuntime
// with the live Target is sim-internal, not in the RenderFrame), so
// the 2D version's static heads are matched by a slow idle spin —
// there is nothing to aim at draw time. If the frame later carries
// the target, swap the spin for a yaw toward it (same shape as
// EnemiesView.headingYaw).
// ─────────────────────────────────────────────────────────────

module TowersView =

  /// Visual scale of tower bodies + weapons (1 = model size on a
  /// 1-unit tile). 0.8 keeps the builds from crowding the tiles —
  /// tune to taste; the weapon mount height follows.
  let towerScale = 0.8f

  /// Tower bodies + weapons go through the shared InstanceScratch:
  /// reset → fill → draw per frame, zero allocation once warm.
  /// The body model for a tower kind + level (complete build parts).
  let private bodyModel (def: TowerDef) (level: int) : ModelInfo =
    let idx = min (max level 1) 6

    let round =
      match idx with
      | 1 -> Models.towerRoundBuildA
      | 2 -> Models.towerRoundBuildB
      | 3 -> Models.towerRoundBuildC
      | 4 -> Models.towerRoundBuildD
      | 5 -> Models.towerRoundBuildE
      | _ -> Models.towerRoundBuildF

    let square =
      match idx with
      | 1 -> Models.towerSquareBuildA
      | 2 -> Models.towerSquareBuildB
      | 3 -> Models.towerSquareBuildC
      | 4 -> Models.towerSquareBuildD
      | 5 -> Models.towerSquareBuildE
      | _ -> Models.towerSquareBuildF

    match def.Key with
    | "cannon" -> square
    | _ -> round

  /// The world-space height of a tower's body top above y = 0 (tile
  /// top 0.2 + the scaled body height) — the HUD's Lv-tag anchor.
  let towerTop (def: TowerDef) (level: int) : float32 =
    0.2f + (bodyModel def level).SizeY * towerScale

  /// Tower bodies and weapons at their cell centers.
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
    let time = Time.now()
    InstanceScratch.reset()

    for KeyValueV(tid, s) in frame.TowerStatics do
      let level =
        frame.TowerLevels
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue 1

      let body = bodyModel s.Def level
      let struct (cx, cy) = s.Cell
      let x = float32 cx + 0.5f
      let z = float32 cy + 0.5f

      // Body: no rotation (the build parts are radially symmetric),
      // scaled by towerScale, base at y = 0.2 (the tile top).
      InstanceScratch.add
        body.Path
        (Matrix.CreateScale towerScale * Matrix.CreateTranslation(x, 0.2f, z))

      // Weapon: on the tower top, slow idle spin with a per-tower
      // phase (see the module header — no target at draw time).
      let rot = time * 0.6f + float32(int tid % 5) * 1.2f
      let wy = 0.2f + body.SizeY * 0.95f * towerScale

      InstanceScratch.add
        s.Def.WeaponModel.Path
        (Matrix.CreateScale towerScale
         * Matrix.CreateRotationY rot
         * Matrix.CreateTranslation(x, wy, z))

    InstanceScratch.draw buffer
