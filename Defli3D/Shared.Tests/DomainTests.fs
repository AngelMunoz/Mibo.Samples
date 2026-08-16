module Defli3D.Tests.DomainTests

open Expecto
open Defli3D.State

let tests =
  testList "Domain" [
    testCase "baked model dataset is consistently indexed" (fun () ->
      // The count itself is asset data (Models.fs is generated) —
      // not pinned. The mechanism: every baked model is reachable
      // by name, names are unique, and the index covers all of it.
      Expect.equal Models.byName.Count Models.all.Length "index covers all"

      Expect.equal
        (Models.all
         |> Array.map(fun m -> m.Name)
         |> Array.distinct
         |> Array.length)
        Models.all.Length
        "names are unique")

    testCase "named accessors resolve to the baked models" (fun () ->
      Expect.equal Models.tileGrass.Name "tile" "tileGrass name"
      Expect.equal Models.tileGrass.SizeX 1f "tileGrass size x"

      Expect.equal Models.roadStraight.Name "tile-straight" "roadStraight"
      Expect.equal Models.weaponCannon.Name "weapon-cannon" "weaponCannon"
      Expect.equal Models.enemyUfoA.Name "enemy-ufo-a" "enemyUfoA"
      Expect.equal Models.ammoBullet.Name "weapon-ammo-bullet" "ammoBullet"
      Expect.equal Models.selectionA.Name "selection-a" "selectionA")

    testCase "tryByName hits and misses" (fun () ->
      Expect.isTrue (Models.tryByName "does_not_exist").IsNone "unknown model"
      Expect.isTrue (Models.tryByName "tile").IsSome "known model"

      // The snow-* models were excluded from the bake — the generator
      // deliberately skips them.
      Expect.isTrue (Models.tryByName "snow-tile").IsNone "snow-tile excluded")

    testCase
      "every baked model: path under BasePath, positive extents"
      (fun () ->
        for m in Models.all do
          Expect.stringStarts m.Path Models.BasePath $"{m.Name} path"
          Expect.isGreaterThan m.SizeX 0f $"{m.Name} size x"
          Expect.isGreaterThan m.SizeY 0f $"{m.Name} size y"
          Expect.isGreaterThan m.SizeZ 0f $"{m.Name} size z")

    testCase "fixture enemy defs are wired to baked models" (fun () ->
      for def in TestData.Fixtures.all do
        Expect.isGreaterThan def.Hp 0 $"{def.Key} hp"
        Expect.isGreaterThan def.Speed 0f $"{def.Key} speed"
        Expect.isGreaterThan def.GoldReward 0 $"{def.Key} reward"

        Expect.isTrue
          (Models.all |> Array.exists(fun m -> m.Name = def.HullModel.Name))
          $"{def.Key} hull baked"

        def.WeaponModel
        |> ValueOption.iter(fun model ->
          Expect.isTrue
            (Models.all |> Array.exists(fun m -> m.Name = model.Name))
            $"{def.Key} weapon baked"))
  ]
