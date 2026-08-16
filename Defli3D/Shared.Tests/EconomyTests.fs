module Defli3D.Tests.EconomyTests

open Expecto
open Mibo.Adaptive
open TestData
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Economy

let tests =
  testList "Economy" [
    testCase "init reads config" (fun () ->
      let m = Economy.init Fixtures.cfg
      Expect.equal (AVal.getValue m.Gold) Fixtures.cfg.StartingGold "gold"
      Expect.equal (AVal.getValue m.Lives) Fixtures.cfg.StartingLives "lives"
      Expect.isFalse (AVal.getValue m.GameOver) "not over")

    testCase "gold spend/earn clamps at zero" (fun () ->
      let m = Economy.init Fixtures.cfg
      Economy.spendGold 1000 m
      Expect.equal (AVal.getValue m.Gold) 0 "clamped at zero"
      Economy.earnGold 25 m
      Expect.equal (AVal.getValue m.Gold) 25 "earned")

    testCase "lives clamp and drive game over" (fun () ->
      let m = Economy.init Fixtures.cfg

      for _ in 1 .. Fixtures.cfg.StartingLives do
        Economy.loseLife m

      Expect.equal (AVal.getValue m.Lives) 0 "lives zero"
      Expect.isTrue (AVal.getValue m.GameOver) "game over"
      Economy.loseLife m
      Expect.equal (AVal.getValue m.Lives) 0 "lives stay clamped")
  ]
