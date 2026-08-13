module BoneProbe.Emit

open System
open System.Globalization
open System.IO
open System.Text
open BoneProbe.Dimensions
open BoneProbe.Scene

// --------------------------------------------------------------
// Dataset emitter
//
// Bakes the Kenney tower-defense kit model measurements (mesh-local
// vertex extents, computed by Dimensions.scanOne with the exact same
// Assimp flag set as the dimensions report) into a compile-time F#
// dataset — no runtime .glb scanning.
//
// Usage: dotnet run --project BoneProbe -- emit <models-dir> <output.fs>
//
// Scans <models-dir>/*.glb (snow-* variants excluded), measures each
// model, sorts by name, and emits a `Defli3D.State.Models` module
// mirroring the Defli/Shared/State/Tiles.fs dataset shape. The curated
// Named/Groups tables below are the single place that assigns semantic
// names to the glb files (same approach as tools/gen-tiles.fsx).
// --------------------------------------------------------------

/// Curated semantic bindings: (binding name, glb filename without extension).
let private named: (string * string)[] = [|
  // Ground tiles
  "tileGrass", "tile"
  "tileDirt", "tile-dirt"
  "tileRock", "tile-rock"
  "tileBump", "tile-bump"
  "tileHill", "tile-hill"
  "tileCrystal", "tile-crystal"
  "tileSlope", "tile-slope"
  "tileTree", "tile-tree"
  "tileTreeDouble", "tile-tree-double"
  "tileTreeQuad", "tile-tree-quad"
  // Roads
  "roadStraight", "tile-straight"
  "roadCornerRound", "tile-corner-round"
  "roadCornerSquare", "tile-corner-square"
  "roadCrossing", "tile-crossing"
  "roadEnd", "tile-end"
  "roadEndRound", "tile-end-round"
  "roadSplit", "tile-split"
  "roadTransition", "tile-transition"
  // Spawn tiles
  "tileSpawn", "tile-spawn"
  "tileSpawnRound", "tile-spawn-round"
  "tileSpawnEnd", "tile-spawn-end"
  "tileSpawnEndRound", "tile-spawn-end-round"
  "spawnRound", "spawn-round"
  "spawnSquare", "spawn-square"
  // Round towers
  "towerRoundBase", "tower-round-base"
  "towerRoundBottomA", "tower-round-bottom-a"
  "towerRoundBottomB", "tower-round-bottom-b"
  "towerRoundBottomC", "tower-round-bottom-c"
  "towerRoundMiddleA", "tower-round-middle-a"
  "towerRoundMiddleB", "tower-round-middle-b"
  "towerRoundMiddleC", "tower-round-middle-c"
  "towerRoundTopA", "tower-round-top-a"
  "towerRoundTopB", "tower-round-top-b"
  "towerRoundTopC", "tower-round-top-c"
  "towerRoundRoofA", "tower-round-roof-a"
  "towerRoundRoofB", "tower-round-roof-b"
  "towerRoundRoofC", "tower-round-roof-c"
  "towerRoundBuildA", "tower-round-build-a"
  "towerRoundBuildB", "tower-round-build-b"
  "towerRoundBuildC", "tower-round-build-c"
  "towerRoundBuildD", "tower-round-build-d"
  "towerRoundBuildE", "tower-round-build-e"
  "towerRoundBuildF", "tower-round-build-f"
  // Square towers
  "towerSquareBottomA", "tower-square-bottom-a"
  "towerSquareBottomB", "tower-square-bottom-b"
  "towerSquareBottomC", "tower-square-bottom-c"
  "towerSquareMiddleA", "tower-square-middle-a"
  "towerSquareMiddleB", "tower-square-middle-b"
  "towerSquareMiddleC", "tower-square-middle-c"
  "towerSquareTopA", "tower-square-top-a"
  "towerSquareTopB", "tower-square-top-b"
  "towerSquareTopC", "tower-square-top-c"
  "towerSquareRoofA", "tower-square-roof-a"
  "towerSquareRoofB", "tower-square-roof-b"
  "towerSquareRoofC", "tower-square-roof-c"
  "towerSquareBuildA", "tower-square-build-a"
  "towerSquareBuildB", "tower-square-build-b"
  "towerSquareBuildC", "tower-square-build-c"
  "towerSquareBuildD", "tower-square-build-d"
  "towerSquareBuildE", "tower-square-build-e"
  "towerSquareBuildF", "tower-square-build-f"
  // Weapons
  "weaponBallista", "weapon-ballista"
  "weaponCannon", "weapon-cannon"
  "weaponCatapult", "weapon-catapult"
  "weaponTurret", "weapon-turret"
  // Ammo
  "ammoArrow", "weapon-ammo-arrow"
  "ammoBoulder", "weapon-ammo-boulder"
  "ammoBullet", "weapon-ammo-bullet"
  "ammoCannonball", "weapon-ammo-cannonball"
  // Enemies
  "enemyUfoA", "enemy-ufo-a"
  "enemyUfoB", "enemy-ufo-b"
  "enemyUfoC", "enemy-ufo-c"
  "enemyUfoD", "enemy-ufo-d"
  "enemyUfoAWeapon", "enemy-ufo-a-weapon"
  "enemyUfoBWeapon", "enemy-ufo-b-weapon"
  "enemyUfoCWeapon", "enemy-ufo-c-weapon"
  "enemyUfoDWeapon", "enemy-ufo-d-weapon"
  "enemyUfoBeam", "enemy-ufo-beam"
  "enemyUfoBeamBurst", "enemy-ufo-beam-burst"
  // Decorations
  "detailCrystal", "detail-crystal"
  "detailCrystalLarge", "detail-crystal-large"
  "detailDirt", "detail-dirt"
  "detailDirtLarge", "detail-dirt-large"
  "detailRocks", "detail-rocks"
  "detailRocksLarge", "detail-rocks-large"
  "detailTree", "detail-tree"
  "detailTreeLarge", "detail-tree-large"
  "woodStructure", "wood-structure"
  "woodStructureHigh", "wood-structure-high"
  // Selection rings
  "selectionA", "selection-a"
  "selectionB", "selection-b"
|]

/// Curated groups over the semantic bindings above.
let private groups: (string * string[])[] = [|
  "groundTiles",
  [|
    "tileGrass"
    "tileDirt"
    "tileRock"
    "tileBump"
    "tileHill"
    "tileCrystal"
    "tileSlope"
    "tileTree"
    "tileTreeDouble"
    "tileTreeQuad"
  |]
  "roadTiles",
  [|
    "roadStraight"
    "roadCornerRound"
    "roadCornerSquare"
    "roadCrossing"
    "roadEnd"
    "roadEndRound"
    "roadSplit"
    "roadTransition"
  |]
  "spawnTiles",
  [|
    "tileSpawn"
    "tileSpawnRound"
    "tileSpawnEnd"
    "tileSpawnEndRound"
    "spawnRound"
    "spawnSquare"
  |]
  "towerRoundParts",
  [|
    "towerRoundBase"
    "towerRoundBottomA"
    "towerRoundBottomB"
    "towerRoundBottomC"
    "towerRoundMiddleA"
    "towerRoundMiddleB"
    "towerRoundMiddleC"
    "towerRoundTopA"
    "towerRoundTopB"
    "towerRoundTopC"
    "towerRoundRoofA"
    "towerRoundRoofB"
    "towerRoundRoofC"
    "towerRoundBuildA"
    "towerRoundBuildB"
    "towerRoundBuildC"
    "towerRoundBuildD"
    "towerRoundBuildE"
    "towerRoundBuildF"
  |]
  "towerSquareParts",
  [|
    "towerSquareBottomA"
    "towerSquareBottomB"
    "towerSquareBottomC"
    "towerSquareMiddleA"
    "towerSquareMiddleB"
    "towerSquareMiddleC"
    "towerSquareTopA"
    "towerSquareTopB"
    "towerSquareTopC"
    "towerSquareRoofA"
    "towerSquareRoofB"
    "towerSquareRoofC"
    "towerSquareBuildA"
    "towerSquareBuildB"
    "towerSquareBuildC"
    "towerSquareBuildD"
    "towerSquareBuildE"
    "towerSquareBuildF"
  |]
  "weapons",
  [| "weaponBallista"; "weaponCannon"; "weaponCatapult"; "weaponTurret" |]
  "ammo", [| "ammoArrow"; "ammoBoulder"; "ammoBullet"; "ammoCannonball" |]
  "enemies", [| "enemyUfoA"; "enemyUfoB"; "enemyUfoC"; "enemyUfoD" |]
  "enemyWeapons",
  [|
    "enemyUfoAWeapon"
    "enemyUfoBWeapon"
    "enemyUfoCWeapon"
    "enemyUfoDWeapon"
  |]
  "decorations",
  [|
    "detailCrystal"
    "detailCrystalLarge"
    "detailDirt"
    "detailDirtLarge"
    "detailRocks"
    "detailRocksLarge"
    "detailTree"
    "detailTreeLarge"
    "woodStructure"
    "woodStructureHigh"
  |]
  "selectionRings", [| "selectionA"; "selectionB" |]
|]

/// Format a measured extent as a float32 literal, rounded to 3 decimals
/// (matches the Dimensions report's F3 output, minus trailing zeros).
let private formatSize(v: float32) =
  MathF.Round(v, 3).ToString("0.0##", CultureInfo.InvariantCulture) + "f"

/// Build the generated file text for the given reports (already loaded).
let private emit (reports: ModelReport[]) (modelsDir: string) =
  let sb = StringBuilder()
  let line(s: string) = sb.AppendLine s |> ignore
  let linef fmt = Printf.ksprintf line fmt

  // Curated-name typo guard: every group member must be a named binding.
  let semanticNames = named |> Array.map fst |> Set.ofArray

  for group, members in groups do
    for m in members do
      if not(Set.contains m semanticNames) then
        failwithf $"group {group} references unknown binding: {m}"

  let byBaseName =
    reports |> Array.map(fun r -> Path.GetFileNameWithoutExtension r.Name, r)

  let findModel(baseName: string) =
    byBaseName
    |> Array.tryFind(fun (n, _) -> n = baseName)
    |> Option.defaultWith(fun () ->
      failwithf $"curated model not found in scan: {baseName}")

  line "// ─────────────────────────────────────────────────────────────"
  line "// GENERATED by BoneProbe emit — DO NOT EDIT BY HAND."

  line
    "// Regenerate with:  dotnet run --project BoneProbe -- emit assets/kenney_tower_defense_kit/Models Defli3D/Shared/State/Models.fs"

  line
    $"// Sources: {modelsDir}/*.glb ({reports.Length} models; snow-* excluded)"

  line "// ─────────────────────────────────────────────────────────────"
  line "namespace Defli3D.State"
  line ""
  line "open System.Collections.Frozen"
  line "open System.Collections.Generic"
  line ""

  line
    "// ModelInfo is hand-defined in Domain.fs (same role as Defli's TileInfo)."

  line ""
  line "module Models ="
  line ""
  line "  [<Literal>]"
  line "  let BasePath = \"kenney_tower_defense_kit/Models\""
  line ""

  line
    "  /// All models baked at compile time (mesh-local extents, sorted by name)."

  line
    "  /// Canonical store: ordered and iterable. The name index is built from it."

  line "  let all: ModelInfo[] = [|"

  for r in
    reports |> Array.sortBy(fun r -> Path.GetFileNameWithoutExtension r.Name) do
    let name = Path.GetFileNameWithoutExtension r.Name

    linef
      "    { Name = %A; Path = BasePath + \"/%s\"; SizeX = %s; SizeY = %s; SizeZ = %s }"
      name
      name
      (formatSize r.SizeX)
      (formatSize r.SizeY)
      (formatSize r.SizeZ)

  line "  |]"
  line ""

  line
    "  /// O(1) name index over the baked dataset (built once at module init)."

  line "  let byName: FrozenDictionary<string, ModelInfo> ="
  line "    all"
  line "    |> Seq.map (fun t -> KeyValuePair(t.Name, t))"
  line "    |> FrozenDictionary.ToFrozenDictionary"
  line ""

  line
    "  /// Safe name lookup for data-driven code (tower/enemy defs, procedural gen)."

  line "  let tryByName (name: string) : ModelInfo voption ="
  line "    Defli3D.FrozenDict.tryGetValue name byName"
  line ""
  line "  // ── Semantically named models (curated in the generator) ──"
  line ""

  for semantic, baseName in named do
    let _, report = findModel baseName

    linef
      "  /// %s — %s × %s × %s."
      baseName
      (formatSize report.SizeX)
      (formatSize report.SizeY)
      (formatSize report.SizeZ)

    linef "  let %s = byName[%A]" semantic baseName

  line ""
  line "  // ── Groups (curated in the generator) ──"
  line ""

  for group, members in groups do
    linef "  let %s = [| %s |]" group (members |> String.concat "; ")

  line ""
  sb.ToString()

/// Bake the dataset and write it to disk. Returns 0 on success, 1 on failure.
let run (modelsDir: string) (outputPath: string) : int =
  let files =
    try
      Directory.GetFiles(modelsDir, "*.glb", SearchOption.TopDirectoryOnly)
    with ex ->
      eprintfn
        $"could not scan models directory: {modelsDir} ({ex.GetType().Name}: {ex.Message})"

      [||]

  let files =
    files |> Array.filter(fun f -> not(Path.GetFileName(f).StartsWith "snow-"))

  match files with
  | [||] ->
    eprintfn
      $"No .glb files found at: {modelsDir} (snow-* variants are excluded)"

    eprintfn
      "Usage: dotnet run --project BoneProbe -- emit <models-dir> <output.fs>"

    1
  | _ ->
    let reports =
      files
      |> Array.sortBy Path.GetFileName
      |> Array.Parallel.map scanOne
      |> Array.sortBy(fun r -> r.Name)

    let failed = reports |> Array.filter(fun r -> not r.Loaded)
    let loaded = reports.Length - failed.Length

    if failed.Length > 0 then
      for r in failed do
        eprintfn $"failed to load: {r.Name} ({r.Error})"

      eprintfn
        $"total={reports.Length} loaded={loaded} failed={failed.Length} — aborting, no output written"

      1
    else
      try
        let dir = Path.GetDirectoryName outputPath

        if not(String.IsNullOrEmpty dir) then
          Directory.CreateDirectory dir |> ignore

        File.WriteAllText(outputPath, emit reports modelsDir)
        printfn $"total={reports.Length} loaded={loaded} failed={failed.Length}"
        printfn $"Wrote {outputPath}"
        0
      with ex ->
        eprintfn
          $"failed to write {outputPath}: {ex.GetType().Name}: {ex.Message}"

        1
