namespace LiveMap.Catalog

/// One baked atlas entry: the sprite's name and its pixel rectangle on
/// the sheet. The generated `Tiles` module fills this record.
type TileInfo = {
  Name: string
  X: int
  Y: int
  Width: int
  Height: int
}

/// One baked model: its logical name, the path the asset service loads
/// (without extension), and its mesh-local extents in model units. The
/// generated `Models` module fills this record; a column's height and a
/// block's footprint come from these numbers, never from a hand-typed
/// constant.
type ModelInfo = {
  Name: string
  Path: string
  SizeX: float32
  SizeY: float32
  SizeZ: float32
}
