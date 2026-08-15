namespace Defli3D.MonoGame

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
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

  /// Tower bodies + guns through the shared InstanceScratch: reset
  /// → fill → draw per frame, zero allocation once warm. Visual
  /// scale + bodies + mounts come from TowerLayout (shared with the
  /// sim's muzzle math).
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
    let time = Time.now()
    InstanceScratch.reset()

    for KeyValueV(tid, s) in frame.TowerStatics do
      let def = s.Def
      let struct (cx, cy) = s.Cell
      let x = float32 cx + 0.5f
      let z = float32 cy + 0.5f
      let center = System.Numerics.Vector2(x, z)
      // Per-tower detailing variant (battery/bunker — deck and keep
      // letters come from the def).
      let variant = TowerLayout.variantSeed cx cy
      let scale = TowerLayout.towerScale

      // The sim's aim: the actual tracked target position, or an
      // idle sweep when the tower holds no target.
      let yaw =
        match frame.TowerAim |> ReadOnlyDict.tryGetValue tid with
        | ValueSome(ValueSome target) ->
          let d = target - center
          MathF.Atan2(d.X, d.Y)
        | _ -> time * 0.6f + float32(int tid % 5) * 1.2f

      // Which pieces rotate: deck towers yaw ONLY the gun deck (the
      // middle piece); keeps yaw whole; everything else is static
      // (its gun rotates instead).
      let rotates(i: int) : bool =
        match def.Chassis with
        | Chassis.Deck _ -> i = 1
        | Chassis.Keep _ -> true
        | _ -> false

      // Body: the chassis's complete piece stack
      // (TowerLayout.stackFor, bottom→top, NO roof), composed with a
      // running UNSCALED height accumulator — each piece rests on
      // the one below, base at y = 0.2 (the tile top). Rotating
      // pieces use the rotate-then-place matrix (around the tower's
      // center axis).
      let mutable acc = 0f
      let pieces = TowerLayout.stackFor def variant

      for i = 0 to pieces.Length - 1 do
        let piece = pieces[i]
        let pieceY = TowerLayout.baseY + acc * scale
        acc <- acc + piece.SizeY

        let matrix =
          if rotates i then
            Matrix.CreateScale scale
            * Matrix.CreateRotationY yaw
            * Matrix.CreateTranslation(x, pieceY, z)
          else
            Matrix.CreateScale scale * Matrix.CreateTranslation(x, pieceY, z)

        InstanceScratch.add piece.Path matrix

      // The gun (gun-carrying chassis only): mounted at the chassis
      // mount height, yawing with the aim, scaled by GunScale (the
      // large guns read large). Decks/keeps are self-armed — no
      // model.
      def.WeaponModel
      |> ValueOption.iter(fun gun ->
        let weaponY = TowerLayout.weaponY def
        let gunScale = scale * def.GunScale

        InstanceScratch.add
          gun.Path
          (Matrix.CreateScale gunScale
           * Matrix.CreateRotationY yaw
           * Matrix.CreateTranslation(x, weaponY, z)))

    InstanceScratch.draw buffer
