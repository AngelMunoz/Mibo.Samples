module Defli3D.Tests.Main

open Expecto

// This exposes the test lists to Expecto's assembly scanner
[<Tests>]
let allTests =
  testList "All" [
    DomainTests.tests
    MapTests.tests
    MapTests.proceduralTests
    MapTests.probeTests
    EnemiesTests.tests
    SpawningTests.tests
    WavesTests.tests
    EconomyTests.tests
    ProjectionTests.tests
    TowersTests.tests
    ProjectilesTests.tests
    ZonesTests.tests
    DiagnosticsTests.tests
    CameraTests.tests
    ApplicationTests.tests
  ]

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssemblyWithCLIArgs [] argv
