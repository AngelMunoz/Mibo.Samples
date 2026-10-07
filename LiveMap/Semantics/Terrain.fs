module LiveMap.Semantics.Terrain

open LiveMap
open LiveMap.Semantics.Palette
open Mibo.Markup

/// Terrain as a per-cell rule: what grows where, decided from the cell's
/// own coordinates.
///
/// Every rule here is a pure function of the coordinates it is asked
/// about, so a document that generates two areas side by side gets a
/// continuous field across the shared edge.
///
/// A rule also paints every cell of its area — a kernel returns one cell
/// per coordinate, and there is no way to paint nothing. So the rules
/// below are the *ground*: the surface a layer holds. The things that
/// stand on it (shrubs, stones, boulders, trees) are elements the
/// document scatters in its decor layer, which is how a cell that carries
/// one reports the layer it stands in.
let meadow: Doc.Kernel<Cell> =
  Doc.Gen2(fun x y ->
    if Noise.chance x y 13 10u then grassC
    elif Noise.chance x y 14 8u then grassB
    else grass)

/// A stand of trees. The canopy comes in patches three cells wide, so
/// the woods have an inside and an edge instead of an even sprinkle.
let forest: Doc.Kernel<Cell> =
  Doc.Gen2(fun x y ->
    let canopy = Noise.chance (x / 3) (y / 3) 21 62u

    if canopy && Noise.chance x y 22 46u then treePine
    elif canopy && Noise.chance x y 23 18u then treeRound
    elif canopy && Noise.chance x y 24 12u then bush
    elif Noise.chance x y 25 75u then grassB
    else grass)

/// Broken ground: sand and gravel. The boulders that go with it are
/// props, so the document scatters them in its decor layer.
let gravel: Doc.Kernel<Cell> =
  Doc.Gen2(fun x y -> if Noise.chance x y 34 20u then sandB else stoneB)

/// A worn surface: mostly one road tile, with end pieces and repairs
/// mixed in. Use it for a one-cell band (`generate 0 4 40 1 road`) to
/// lay a route, or over an area for a paved yard.
let road: Doc.Kernel<Cell> =
  Doc.Gen2(fun x y ->
    if Noise.chance x y 41 6u then pathEndLeftDirt
    elif Noise.chance x y 42 6u then pathEndUpDirt
    elif Noise.chance x y 43 8u then dirtC
    else pathHorizontalDirt)

/// The same route running the other way.
let track: Doc.Kernel<Cell> =
  Doc.Gen2(fun x y ->
    if Noise.chance x y 51 6u then pathEndUpDirt
    elif Noise.chance x y 52 8u then dirtB
    else pathVerticalDirt)
