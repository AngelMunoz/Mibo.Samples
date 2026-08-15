module Defli3D.Tests.ZonesTests

open System.Collections.Generic
open System.Numerics
open Expecto
open Mibo.Adaptive
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open TestData
open Defli3D.State.Systems.Zones

let private model() = Zones.init()

/// One enemy at a position (transient Positions-shaped dict).
let private positionsAt (ids: int<EnemyId>[]) (pos: Vector2[]) =
  let d = Dictionary<int<EnemyId>, Vector2>()

  for i = 0 to ids.Length - 1 do
    d[ids[i]] <- pos[i]

  d

/// A zone def with distinct values (0.5 s ticks, slow 0.5).
let private zoneDef = {
  Radius = 1f
  Seconds = 2f
  Slow = 0.5f
  TickDamage = 3
  TickInterval = 0.5f
  MaxStacks = 5
}

let tests =
  testList "Zones" [
    testCase "Drop adds a row armed with an immediate tick" (fun () ->
      let m = model()

      Zones.handle (ZoneMsg.Drop(Vector2(2f, 2f), zoneDef)) m

      match m.Rows |> CMap.tryGetValue(0<ZoneId>) with
      | ValueSome row ->
        Expect.equal row.Pos (Vector2(2f, 2f)) "pos"
        Expect.equal row.Remaining zoneDef.Seconds "life armed"
        Expect.equal row.TickTimer 0f "ticks immediately"
      | ValueNone -> failtest "row must exist")

    testCase "tick applies slow + DoT to enemies inside" (fun () ->
      let m = model()

      Zones.handle (ZoneMsg.Drop(Vector2.Zero, zoneDef)) m

      let eid = 0<EnemyId>
      let positions = positionsAt [| eid |] [| Vector2(0.5f, 0f) |]

      // First tick fires immediately (timer 0).
      let applies = Zones.tick 0.1f m positions

      match applies with
      | [| a |] ->
        Expect.equal a.Enemy eid "enemy inside"
        Expect.equal a.Damage 3 "DoT applied"
        Expect.equal a.SlowFactor 0.5f "slow applied"
        Expect.isTrue (a.SlowSeconds > 0f) "slow armed with a horizon"
      | _ -> failtest "expected exactly one application"

      // The row survives (2 s life).
      Expect.equal ((m.Rows |> AMap.getValue).Count) 1 "zone alive")

    testCase "enemies outside the radius are untouched" (fun () ->
      let m = model()

      Zones.handle
        (ZoneMsg.Drop(Vector2.Zero, { zoneDef with Radius = 0.5f }))
        m

      let positions =
        positionsAt [| 0<EnemyId>; 1<EnemyId> |] [|
          Vector2(0.4f, 0f) // inside 0.5
          Vector2(2f, 0f) // outside
        |]

      let applies = Zones.tick 0.1f m positions

      match applies with
      | [| a |] -> Expect.equal a.Enemy (0<EnemyId>) "only the inside enemy"
      | _ -> failtest "expected exactly one application")

    testCase
      "damage stacks across zones up to MaxStacks; slow takes the strongest"
      (fun () ->
        let m = model()

        // Two overlapping zones at the same spot: one weak+slow, one
        // strong slow. MaxStacks = 5 allows both.
        let weak = {
          zoneDef with
              TickDamage = 2
              Slow = 0.8f
        }

        let strong = {
          zoneDef with
              TickDamage = 3
              Slow = 0.5f
        }

        Zones.handle (ZoneMsg.Drop(Vector2.Zero, weak)) m
        Zones.handle (ZoneMsg.Drop(Vector2.Zero, strong)) m

        let eid = 0<EnemyId>

        let applies =
          Zones.tick 0.1f m (positionsAt [| eid |] [| Vector2.Zero |])

        match applies with
        | [| a |] ->
          Expect.equal a.Damage 5 "both zones' damage stacked"
          Expect.equal a.SlowFactor 0.5f "strongest slow wins"
        | _ -> failtest "expected exactly one application"

        // Six zones, MaxStacks 5 → the sixth contributes nothing.
        let m2 = model()

        for _ in 1..6 do
          Zones.handle (ZoneMsg.Drop(Vector2.Zero, zoneDef)) m2

        let applies2 =
          Zones.tick 0.1f m2 (positionsAt [| eid |] [| Vector2.Zero |])

        match applies2 with
        | [| a |] ->
          Expect.equal a.Damage (5 * zoneDef.TickDamage) "capped at 5 stacks"
        | _ -> failtest "expected exactly one application")

    testCase "tick interval gates damage ticks between applications" (fun () ->
      let m = model()

      Zones.handle (ZoneMsg.Drop(Vector2.Zero, zoneDef)) m

      let eid = 0<EnemyId>
      let positions = positionsAt [| eid |] [| Vector2.Zero |]

      // First tick fires; then 0.1 s later the timer has NOT expired
      // (interval 0.5) → no application.
      let _ = Zones.tick 0.1f m positions
      let none = Zones.tick 0.1f m positions

      Expect.isEmpty none "interval gates the next tick"

      // 0.4 s more (0.5 since the armed tick) → the next application.
      let next = Zones.tick 0.4f m positions

      Expect.equal (Array.length next) 1 "next tick fires on schedule")

    testCase "the zone expires after its life" (fun () ->
      let m = model()

      Zones.handle (ZoneMsg.Drop(Vector2.Zero, zoneDef)) m

      let eid = 0<EnemyId>
      let positions = positionsAt [| eid |] [| Vector2.Zero |]

      // 2.5 s total > 2 s life.
      for _ in 1..25 do
        Zones.tick 0.1f m positions |> ignore

      Expect.equal ((m.Rows |> AMap.getValue).Count) 0 "expired")
  ]
