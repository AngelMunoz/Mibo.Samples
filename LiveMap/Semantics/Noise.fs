namespace LiveMap.Semantics

/// A small deterministic hash, so a document paints the same map on
/// every run and on every .NET version.
///
/// Flow's own scatter styles ride the BCL random generator, whose
/// sequence may change between .NET versions; these rules do not. A rule
/// that decides content from a cell's own coordinates is also seamless:
/// two areas generated side by side agree along their shared edge, which
/// a pattern baked from a stamp cannot do.
module Noise =

  /// A hash of a cell and a salt. The salt separates one rule from
  /// another that runs over the same cells.
  let inline hash (x: int) (y: int) (salt: int) : uint32 =
    let mutable h =
      uint32 x * 374761393u + uint32 y * 668265263u + uint32 salt * 2246822519u

    h <- (h ^^^ (h >>> 13)) * 1274126177u
    h ^^^ (h >>> 16)

  /// True for roughly `percent` cells in a hundred.
  let inline chance (x: int) (y: int) (salt: int) (percent: uint32) : bool =
    hash x y salt % 100u < percent

  /// A fraction in [0, 1) for a cell: heights, weights, and gradients.
  let inline fraction (x: int) (y: int) (salt: int) : float32 =
    float32(hash x y salt % 10000u) / 10000.0f
