module FPSSample.MonoGL.Program

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open FPSSample.Types
open FPSSample.MonoShared

[<EntryPoint>]
let main _ =
  let mgProgram =
    Program.create()
    |> MonoGameProgram.ofProgram
    |> MonoGameProgram.withConfig(fun (game, _graphics) ->
      game.Content.RootDirectory <- "Content"
      game.IsMouseVisible <- false)

  use game = new MiboGame<GameModel, Msg>(mgProgram)
  game.Run()
  0
