module Defli3D.Tests.WavesTests

open Expecto
open Mibo.Adaptive
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Waves

// The fixture map's calibrated capacity — every wave-scale pin is
// computed against the SAME saturation the sim uses, so the pins
// verify the wiring (curve → composeWave → defs), not frozen numbers.
let private capacity = Balance.capacityOf(MapModel.create TestData.Fixtures.cfg)

let private sat = capacity.Saturation

let tests =
  testList "Waves" [
    testCase "composeWave scales with wave number" (fun () ->
      let w1 = Waves.composeWave sat 1
      let w5 = Waves.composeWave sat 5
      Expect.isGreaterThan w5.Count w1.Count "count grows"
      Expect.isLessThan w5.Interval w1.Interval "interval shrinks"
      Expect.isGreaterThanOrEqual w5.Interval 0.3f "interval floors"
      Expect.isGreaterThan w1.Count 0 "non-empty")

    testCase "difficulty tiers scale every 5 waves" (fun () ->
      // Waves 1-4: base stats (tier 0 is exactly unscaled). Wave 5+:
      // the logistic curves at tier = wave/5, calibrated by the map.
      let hpOf(w: WaveDef) =
        let struct (def, _) = w.Table[0]
        def.Hp

      let w1 = Waves.composeWave sat 1
      let w5 = Waves.composeWave sat 5
      let w10 = Waves.composeWave sat 10

      Expect.equal (hpOf w1) EnemyDefs.grunt.Hp "wave 1 unscaled"

      let scaleOf n = Balance.scaleOfWave sat n

      Expect.equal
        (hpOf w5)
        (max 1f (EnemyDefs.grunt.Hp * (scaleOf 5).Hp))
        "wave 5 tier-scaled"

      Expect.equal
        (hpOf w10)
        (max 1f (EnemyDefs.grunt.Hp * (scaleOf 10).Hp))
        "wave 10 tier-scaled"

      // Rewards follow demand^Beta and never collapse to zero.
      let rewardOf(w: WaveDef) =
        let struct (def, _) = w.Table[0]
        def.GoldReward

      Expect.equal
        (rewardOf w10)
        (max
          1
          (int(float EnemyDefs.grunt.GoldReward * float (scaleOf 10).Reward)))
        "reward scaled"

      // The tier's resistance combines with the def's innate one.
      let struct (def5, _) = w5.Table[0]

      Expect.equal
        def5.Resist
        (1f - (1f - EnemyDefs.grunt.Resist) * (1f - (scaleOf 5).Resist))
        "innate and tier resist combined"

      // The Scale aval follows WaveNumber (the projection contract).
      let m = Waves.init capacity
      Expect.equal (AVal.getValue m.Scale).Hp 1f "base scale"
      m.WaveNumber.Set 10

      Expect.equal (AVal.getValue m.Scale).Hp (scaleOf 10).Hp "tier 2 scale")

    testCase "fliers enter the tables from wave 4" (fun () ->
      let w4 = Waves.composeWave sat 4

      Expect.contains
        (w4.Table |> Array.map(fun struct (def, _) -> def.Key))
        EnemyDefs.flier.Key
        "wave 4 has fliers"

      let w5 = Waves.composeWave sat 5

      Expect.contains
        (w5.Table |> Array.map(fun struct (def, _) -> def.Key))
        EnemyDefs.flier.Key
        "boss wave has fliers"

      let w2 = Waves.composeWave sat 2

      Expect.isFalse
        (w2.Table
         |> Array.exists(fun struct (def, _) -> def.Key = EnemyDefs.flier.Key))
        "early waves have no fliers")

    testCase "composition is deterministic (no RNG in the director)" (fun () ->
      let tableOf n =
        Waves.composeWave sat n
        |> fun w -> w.Table |> Array.map(fun struct (def, _) -> def.Key)

      Expect.equal (tableOf 4) (tableOf 4) "wave 4 table stable"
      Expect.equal (tableOf 5) (tableOf 5) "wave 5 table stable"
      Expect.equal (tableOf 12) (tableOf 12) "wave 12 table stable")

    testCase "boss waves (every 5th) lead with a tier-scaled boss" (fun () ->
      let w5 = Waves.composeWave sat 5
      Expect.hasLength w5.ExtraSpawns 1 "one extra spawn"

      let struct (bossDef, delay) = w5.ExtraSpawns[0]
      Expect.equal bossDef.Archetype EnemyArchetype.Boss "boss leads"
      Expect.equal delay 1.5f "spawns with the initial delay"

      // Expected values come from the same calibrated curve the
      // director uses — the pin verifies wiring, not frozen numbers.
      let expectedHp n =
        max 1f (EnemyDefs.boss.Hp * (Balance.scaleOfWave sat n).Hp)

      Expect.equal bossDef.Hp (expectedHp 5) "tier-scaled hp"

      let w10 = Waves.composeWave sat 10
      let struct (boss10, _) = w10.ExtraSpawns[0]

      Expect.equal boss10.Hp (expectedHp 10) "wave 10 tier-scaled"

      // Regular waves have no extras.
      for n in [ 1; 2; 3; 4; 6; 7 ] do
        Expect.isEmpty
          (Waves.composeWave sat n).ExtraSpawns
          $"wave %d{n} no extras")

    testCase "StartNextWave composes + activates, then refuses" (fun () ->
      let m = Waves.init capacity
      let events = Waves.startNextWave m
      let m' = m

      match events with
      | [| WaveStarted wave |] ->
        Expect.equal wave.Count (Waves.composeWave sat 1).Count "wave 1"
      | _ -> failtest "expected WaveStarted"

      Expect.isTrue m'.WaveActive.Value "active"
      Expect.equal m'.WaveNumber.Value 1 "wave number"

      let events = Waves.startNextWave m'
      let m2 = m'
      Expect.equal events.Length 0 "refuses while active"
      Expect.equal m2.WaveNumber.Value 1 "wave number unchanged")

    testCase "clear detection via direct values" (fun () ->
      let m = Waves.init capacity
      let _ = Waves.startNextWave m
      let m' = m

      // Still spawning: no clear.
      let events = Waves.tick m' (AVal.constant 3) false
      let m2 = m'
      Expect.equal (events |> Seq.length) 0 "not cleared with enemies alive"

      // Queue empty and no enemies: cleared.
      let events = Waves.tick m2 (AVal.constant 0) true
      let m3 = m2

      match events |> Seq.tryHead with
      | Some WaveCleared -> ()
      | _ -> failtest "expected WaveCleared"

      Expect.isFalse m3.WaveActive.Value "inactive after clear")

    testCase "banner projection follows state" (fun () ->
      let m = Waves.init capacity

      Expect.stringContains
        (AVal.getValue m.Banner)
        "Press Enter"
        "idle banner"

      let _ = Waves.startNextWave m
      let m' = m
      Expect.stringContains (AVal.getValue m'.Banner) "Wave 1" "active banner")
  ]
