namespace Defli3D.Raylib

open System
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// TowersView — tower bodies + mounted guns from the frame's
// TowerStatics/TowerAim snapshots, all instanced (one draw per
// model, not per tower). Every chassis is COMPLETE from placement
// (a level-up is power, never height).
//
// Rotation is driven by the SIM's aim (the TowerAim projection —
// Runtimes.Aim, the actual tracked target), not a view-side guess:
//   Deck   — ONLY the gun deck (the middle piece) yaws; the bottom
//            and the top above it stay put.
//   Keep   — the whole prebuilt tower yaws (it fires through its
//            opening).
//   Bunker / Battery / Emplacement — the body is static; the GUN
//            model yaws at its mount (pad / bay floor / stack top),
//            scaled by the def's GunScale (large guns read large).
// Decks and keeps are self-armed (WeaponModel = None) — their ammo
// leaves from TowerLayout.muzzleY at the tower's edge.
//
// No target → the rotating parts idle with a slow per-tower-phase
// sweep.
// ─────────────────────────────────────────────────────────────

module TowersView =

  let view
    (ctx: GameContext)
    (statics: IReadOnlyDictionary<int<TowerId>, TowerStatic>)
    (levels: IReadOnlyDictionary<int<TowerId>, int>)
    (aim: IReadOnlyDictionary<int<TowerId>, Vector2 voption>)
    (buffer: RenderBuffer3D)
    =
    let time = float32(Raylib.GetTime())
    InstanceScratch.reset()

    for KeyValueV(tid, s) in statics do
      let def = s.Def
      let center = Cells.center s.Cell (Vector2.One)
      let cx = center.X
      let cy = center.Y
      let struct (ix, iy) = s.Cell
      // Per-tower detailing variant (battery/bunker — deck and keep
      // letters come from the def).
      let variant = TowerLayout.variantSeed ix iy
      let scale = TowerLayout.towerScale

      // The sim's aim: the actual tracked target position, or an
      // idle sweep when the tower holds no target.
      let yaw =
        match aim |> ReadOnlyDict.tryGetValue tid with
        | ValueSome(ValueSome target) ->
          let d = target - center
          MathF.Atan2(d.X, d.Y)
        | _ -> time * 0.5f + float32(int(tid % 7<TowerId>))

      // Which pieces rotate: deck towers yaw ONLY the gun deck (the
      // middle piece); keeps yaw whole; everything else is static
      // (its gun rotates instead).
      let rotates(i: int) : bool =
        match def.Chassis with
        | Chassis.Deck _ -> i = 1
        | Chassis.Keep _ -> true
        | _ -> false

      // Body — the chassis's complete piece stack
      // (TowerLayout.stackFor, bottom→top, NO roof), one instanced
      // draw per piece, laid on the tile top (TowerLayout.baseY).
      // Rotating pieces use the rotate-then-place matrix (around the
      // tower's center axis).
      let mutable acc = 0f

      let pieces = TowerLayout.stackFor def variant

      for i = 0 to pieces.Length - 1 do
        let piece = pieces[i]
        let pieceY = TowerLayout.baseY + acc * scale
        acc <- acc + piece.SizeY

        let matrix =
          if rotates i then
            Raymath.MatrixMultiply(
              Raymath.MatrixMultiply(
                Raymath.MatrixScale(scale, scale, scale),
                Raymath.MatrixRotateY(yaw)
              ),
              Raymath.MatrixTranslate(cx, pieceY, cy)
            )
          else
            Raymath.MatrixMultiply(
              Raymath.MatrixScale(scale, scale, scale),
              Raymath.MatrixTranslate(cx, pieceY, cy)
            )

        InstanceScratch.add piece.Name matrix

      // The gun (gun-carrying chassis only): mounted at the chassis
      // mount height, yawing with the aim, scaled by GunScale (the
      // large guns read large). Decks/keeps are self-armed — no
      // model.
      def.WeaponModel
      |> ValueOption.iter(fun gun ->
        let weaponY = TowerLayout.weaponY def
        let gunScale = scale * def.GunScale

        InstanceScratch.add
          gun.Name
          (Raymath.MatrixMultiply(
            Raymath.MatrixMultiply(
              Raymath.MatrixScale(gunScale, gunScale, gunScale),
              Raymath.MatrixRotateY(yaw)
            ),
            Raymath.MatrixTranslate(cx, weaponY, cy)
          )))

    InstanceScratch.draw buffer
