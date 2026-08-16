module Defli.State.Systems.Economy

open Mibo.Adaptive
open Defli
open Defli.State

// ─────────────────────────────────────────────────────────────
// Economy sub-system — two singletons, one system. No events out
// (nothing consumes economy output except the view/Application); kills
// and arrivals reach it via Application-translated events.
// ─────────────────────────────────────────────────────────────

type EconomyModel() =
  member val Gold = CVal.create 0 with get, set
  member val Lives = CVal.create 0 with get, set
  // Own projection (showcase #4): game over.
  member val GameOver: aval<bool> = Unchecked.defaultof<_> with get, set

module Economy =

  let init(cfg: WorldConfig) : EconomyModel =
    let m = EconomyModel()
    m.Gold.Set cfg.StartingGold
    m.Lives.Set cfg.StartingLives

    m.GameOver <- m.Lives |> AVal.map(fun lives -> lives <= 0)

    m

  let inline spendGold (amount: int) (model: EconomyModel) : unit =
    let gold = model.Gold |> AVal.getValue
    model.Gold.UpdateTo(max 0 (gold - amount)) |> ignore

  let inline earnGold (amount: int) (model: EconomyModel) : unit =
    let gold = model.Gold |> AVal.getValue
    model.Gold.UpdateTo(gold + amount) |> ignore

  let inline loseLife(model: EconomyModel) : unit =
    let lives = model.Lives |> AVal.getValue
    model.Lives.UpdateTo(max 0 (lives - 1)) |> ignore
