# The asset tables

Three files in this folder hold every asset the sample can draw, with the numbers that place it. The tables are generated, committed, and read at compile time. No file is parsed at run time.

| File | Contents |
| --- | --- |
| `Types.fs` | the two records: `TileInfo` and `ModelInfo` |
| `Tiles.fs` | the tile atlas: 299 entries, 57 named words, 14 groups |
| `Models.fs` | 153 models with their mesh extents |
| `Tiles.curation` | the input that names the tiles |
| `Models.fsi` | the signature of the model table |

## The tile table

`Tiles.fs` comes from the Kenney tower-defense sheet. It holds one entry for each of the 299 tiles: the name, the position on the sheet, and the size.

`Tiles.all` is the whole atlas in sheet order. `Tiles.byName` is an index for a lookup by name. `Tiles.tryByName` returns `ValueNone` for a name that is not in the atlas. The named bindings at the end of the file are the 57 words the palette uses.

`Tiles.SheetPath` states the image the game loads. `Tiles.TileSize` states the width of one tile in pixels.

Regenerate the table after the sheet changes:

```bash
dotnet fsi tools/gen-tiles.fsx \
  --xml assets/kenney_tower-defense-top-down/towerDefense_tilesheet.xml \
  --out LiveMap/Catalog/Tiles.fs --namespace LiveMap.Catalog --module Tiles \
  --sheet-path kenney_tower-defense-top-down/towerDefense_tilesheet.png \
  --tile-size --curation LiveMap/Catalog/Tiles.curation
```

`Tiles.curation` holds two kinds of line. A `binding` gives a short name to one atlas entry. A `group` collects bindings under one name.

```text
binding grass = grass_full_a
group groundGrass = grass, grassB, grassC
```

The generator fails on a binding that names a tile the atlas does not hold, and on a group that names an unknown binding. The command in the header of `Tiles.fs` is the command that produced the file.

## The model table

`Models.fs` comes from the Kenney platformer kit. It holds one entry for each of the 153 models: the name, the path the asset service loads, and the extents of the mesh on X, Y and Z in model units.

`Models.BasePath` is the folder prefix that every path shares. `Models.tryByName` looks a model up by name.

Regenerate the table with the BoneProbe tool:

```bash
dotnet run --project BoneProbe -- emit assets/kenney_platformer-kit/Models \
  LiveMap/Catalog/Models.fs --namespace LiveMap.Catalog \
  --base-path assets/kenney_platformer-kit/Models --fsi
```

BoneProbe reads the vertices of each model, so the extents are the size of the mesh and not a value typed by hand. The blocks mode divides a cell by the extents, so a model lands on one cell whatever its size.

## The records

`Types.fs` declares the two records the generators fill. A generator writes only the tables; the records stay in F#, where the compiler checks them.

```fsharp
type TileInfo = { Name: string; X: int; Y: int; Width: int; Height: int }

type ModelInfo = {
  Name: string
  Path: string
  SizeX: float32
  SizeY: float32
  SizeZ: float32
}
```
