module Platformer.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssemblyWithCLIArgs [] argv
