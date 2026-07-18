module ModelProbe.WindowsDX12.Program

open Mibo.Elmish
open ModelProbe

[<EntryPoint>]
let main _ =
  let mgProgram = ModelProbe.create()

  use game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
