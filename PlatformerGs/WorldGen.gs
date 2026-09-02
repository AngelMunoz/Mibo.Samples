// file: WorldGen.gs
// Procedural chunk generation with reachability guarantees.
// Ported from Platformer/Shared/WorldGen.fs; Mibo.Layout's LayeredGrid2D /
// GridSection2D replaced by a plain Dictionary<int32, TileV> per chunk
// (keyed by chunkTileKey(x, y); absent = Empty).

package PlatformerGs

import System
import System.Numerics
import System.Collections.Generic
import Raylib_cs

// ── Default config (WorldGen.defaultConfig) ──────────────────

func defaultConfig() GenConfig {
    return GenConfig{
        Ground: GroundConfig{
            MinSlabs: 1, MaxSlabs: 5,
            MinWidth: 6, MaxWidth: 14,
            MinHeight: 2, MaxHeight: 4,
            MinGap: 2, MaxGap: 4
        },
        Platform: PlatformConfig{
            MinCount: 2, MaxCount: 5,
            MinWidth: 2, MaxWidth: 7,
            MinClearance: 3, MaxClearance: 4,
            MinVerticalGap: 3, MaxVerticalGap: 4
        },
        BiomeColumnScale: 0.03f,
        ElevationScale: 0.04f,
        ElevationAmplitude: 2
    }
}

// ── Reachability (physics-derived jump predicate) ────────────

// Tile Y of the ground surface within every chunk.
func groundYTile() int32 { return int32(worldHeight()) }

// Ceiling Y — nothing generates above this.
func skyCeiling() int32 { return groundYTile() - 10 }

// Max same-level horizontal jump reach in tiles (JumpBudget.MaxHorizontalTiles).
func maxJumpTiles() int32 { return 4 }

// Height (in tiles) reached above the launch surface at horizontal distance
// `distanceTiles` for a fully-held running jump.
func arcHeightTiles(distanceTiles float32) float32 {
    var d = distanceTiles * tileSize()
    var t = d / moveSpeed()
    return (-jumpSpeed() * t - 0.5f * gravity() * t * t) / tileSize()
}

func reachable(gapTiles float32, riseTiles float32) bool {
    var effectiveGap = gapTiles
    if effectiveGap < 1f { effectiveGap = 1f }
    return arcHeightTiles(effectiveGap) >= riseTiles
}

func reachableBoth(gapTiles float32, dyTiles float32) bool {
    return reachable(gapTiles, dyTiles) && reachable(gapTiles, -dyTiles)
}

// ── Value noise — biomes + elevation ─────────────────────────

func chunkSeed(cx int32, cy int32, worldSeed int32) int32 {
    return (cx * 73856093) ^ (cy * 19349663) ^ worldSeed
}

func hash01(x int32, y int32, seed int32) float32 {
    // Parity note: F#'s `>>>` on signed int32 sign-propagates (arithmetic
    // shift), so G#'s plain arithmetic `>>` is the matching operator here.
    var h int32 = (x * 374761393) ^ (y * 668265263) ^ (seed * 1442695041)
    h = h ^ (h >> 13)
    h = h * 1274126177
    h = h ^ (h >> 16)
    var m = h % 1000
    if m < 0 { m = -m }
    return float32(m) / 1000f
}

func smoothstep(t float32) float32 {
    return t * t * (3f - 2f * t)
}

func biomeNoise(cx float32, cy float32, scale float32, seed int32) float32 {
    var fx = cx * scale
    var fy = cy * scale
    var x0 = int32(MathF.Floor(fx))
    var y0 = int32(MathF.Floor(fy))
    var sx = smoothstep(fx - float32(x0))
    var sy = smoothstep(fy - float32(y0))

    var n00 = hash01(x0, y0, seed)
    var n10 = hash01(x0 + 1, y0, seed)
    var n01 = hash01(x0, y0 + 1, seed)
    var n11 = hash01(x0 + 1, y0 + 1, seed)

    var top = n00 + (n10 - n00) * sx
    var bot = n01 + (n11 - n01) * sx
    return top + (bot - top) * sy
}

// Biome at a world-tile column — smooth across chunk seams.
func biomeAtColumn(worldX int32, seed int32, scale float32) Biome {
    var n = biomeNoise(float32(worldX), 0f, scale, seed)
    var idx = int32(n * 6f)
    if idx > 5 { idx = 5 }
    switch idx {
    case 0 { return Biome.Grass }
    case 1 { return Biome.Dirt }
    case 2 { return Biome.Stone }
    case 3 { return Biome.Snow }
    case 4 { return Biome.Sand }
    default { return Biome.Purple }
    }
}

// Surface tile-Y for a world column (band-limited noise, spawn protected).
func elevationAtColumn(worldX int32, seed int32, scale float32, amplitude int32) int32 {
    if amplitude <= 0 || worldX < spawnProtectedCells() {
        return groundYTile()
    }
    var n = biomeNoise(float32(worldX), 0f, scale, seed ^ 0x5A5A5A5A)
    var offset = int32(MathF.Round(n * float32(2 * amplitude + 1))) - amplitude
    return groundYTile() - offset
}

// ── Ground planning (WorldGen.Ground.plan) ───────────────────

// Clamp `targetY` so the rise from `py` across `gap` tiles stays inside the
// jump arc — the guarantee that generated terrain is always reachable.
func clampRise(gap int32, py int32, targetY int32) int32 {
    var safeRise = int32(MathF.Floor(arcHeightTiles(float32(gap))))
    var minY = py - safeRise
    var maxY = py + safeRise
    var y = targetY
    if y < minY { y = minY }
    if y > maxY { y = maxY }
    return y
}

// Plan ground slabs across a `width`-wide region, clamping each slab's
// surface Y so the rise from the previous slab stays inside the jump arc.
func planGround(rng Random, cfg GroundConfig, maxHorizontalTiles int32, width int32, elevationAt Func[int32, int32]) []GroundSpec {
    var specs = List[GroundSpec]()
    var maxGap = cfg.MaxGap
    if maxHorizontalTiles < maxGap { maxGap = maxHorizontalTiles }

    var x int32 = 0
    var prevY = elevationAt(0)
    var stop = false

    while !stop && specs.Count < cfg.MaxSlabs {
        var gap = 0
        if specs.Count > 0 {
            gap = rng.Next(cfg.MinGap, maxGap + 1)
        }
        x = x + gap
        var remaining = width - x
        if remaining < cfg.MinWidth {
            stop = true
        } else {
            var upper = cfg.MaxWidth + 1
            if remaining + 1 < upper { upper = remaining + 1 }
            var w = rng.Next(cfg.MinWidth, upper)
            var h = rng.Next(cfg.MinHeight, cfg.MaxHeight + 1)
            var targetY = elevationAt(x)
            var y = targetY
            if specs.Count > 0 {
                y = clampRise(gap, prevY, targetY)
            }
            specs.Add(GroundSpec{X: x, Y: y, W: w, H: h})
            prevY = y
            x = x + w
            if width - x <= maxGap && specs.Count >= cfg.MinSlabs {
                stop = true
            }
        }
    }

    // Reset x to the last slab's actual right edge.
    if specs.Count > 0 {
        var last = specs[specs.Count - 1]
        x = last.X + last.W
    }

    // Keep the trailing gap within the jump budget.
    while width - x > maxGap {
        var gap = rng.Next(cfg.MinGap, maxGap + 1)
        var bridgeX = x + gap
        var bridgeRemaining = width - bridgeX
        if bridgeRemaining < cfg.MinWidth {
            if specs.Count > 0 {
                var i = specs.Count - 1
                var last = specs[i]
                var trailingGap = rng.Next(cfg.MinGap, maxGap + 1)
                last.W = last.W + (width - (last.X + last.W) - trailingGap)
                specs[i] = last
            }
            x = width
        } else {
            var upper = cfg.MaxWidth + 1
            if bridgeRemaining + 1 < upper { upper = bridgeRemaining + 1 }
            var w = rng.Next(cfg.MinWidth, upper)
            var h = rng.Next(cfg.MinHeight, cfg.MaxHeight + 1)
            var targetY = elevationAt(bridgeX)
            var y = clampRise(gap, prevY, targetY)
            specs.Add(GroundSpec{X: bridgeX, Y: y, W: w, H: h})
            prevY = y
            x = bridgeX + w
        }
    }

    return specs.ToArray()
}

// Clamp the last slab's Y so the cross-seam edge to the next chunk's first
// slab is reachable while preserving intra-chunk reachability.
func clampCrossSeam(specs []GroundSpec, chunkWidth int32, nextFirstSlabY int32) []GroundSpec {
    if specs.Length == 0 {
        return specs
    }
    var i = specs.Length - 1
    var last = specs[i]
    var trailingGap = chunkWidth - (last.X + last.W)
    if trailingGap <= 0 {
        return specs
    }
    var crossSafeRise = int32(MathF.Floor(arcHeightTiles(float32(trailingGap))))
    var crossMinY = nextFirstSlabY - crossSafeRise
    var crossMaxY = nextFirstSlabY + crossSafeRise

    var lo = crossMinY
    var hi = crossMaxY
    if i >= 1 {
        var prev = specs[i - 1]
        var intraGap = last.X - (prev.X + prev.W)
        var intraSafeRise = int32(MathF.Floor(arcHeightTiles(float32(intraGap))))
        var intraMinY = prev.Y - intraSafeRise
        var intraMaxY = prev.Y + intraSafeRise
        if intraMinY > lo { lo = intraMinY }
        if intraMaxY < hi { hi = intraMaxY }
    }

    var clampedY = last.Y
    if clampedY < lo { clampedY = lo }
    if clampedY > hi { clampedY = hi }
    last.Y = clampedY
    specs[i] = last
    return specs
}

// ── Tile stamping (Stamps.fs) ────────────────────────────────

// Horizontal row with distinct start/middle/end tiles.
func stampHRow(tiles Dictionary[int32, TileV], ox int32, oy int32, length int32, startTile TileV, middleTile TileV, endTile TileV, singleTile TileV) {
    if length <= 0 {
        return
    }
    if length == 1 {
        tiles[chunkTileKey(ox, oy)] = singleTile
        return
    }
    tiles[chunkTileKey(ox, oy)] = startTile
    for var i = 1; i <= length - 2; i++ {
        tiles[chunkTileKey(ox + i, oy)] = middleTile
    }
    tiles[chunkTileKey(ox + length - 1, oy)] = endTile
}

func t(kind TileKind, biome Biome) TileV {
    return TileV{Kind: kind, Biome: biome}
}

func stampGround(tiles Dictionary[int32, TileV], ox int32, oy int32, biome Biome, w int32, h int32) {
    // Top row: BlockTopLeft / BlockTop / BlockTopRight (single → Block).
    stampHRow(tiles, ox, oy, w,
        t(TileKind.BlockTopLeft, biome), t(TileKind.BlockTop, biome), t(TileKind.BlockTopRight, biome), t(TileKind.Block, biome))
    // Middle rows: BlockLeft / BlockCenter / BlockRight.
    for var row = 1; row <= h - 2; row++ {
        stampHRow(tiles, ox, oy + row, w,
            t(TileKind.BlockLeft, biome), t(TileKind.BlockCenter, biome), t(TileKind.BlockRight, biome), t(TileKind.Block, biome))
    }
    // Bottom row: BlockBottomLeft / BlockBottom / BlockBottomRight.
    if h >= 2 {
        stampHRow(tiles, ox, oy + h - 1, w,
            t(TileKind.BlockBottomLeft, biome), t(TileKind.BlockBottom, biome), t(TileKind.BlockBottomRight, biome), t(TileKind.Block, biome))
    }
}

func stampPlatform(tiles Dictionary[int32, TileV], ox int32, oy int32, biome Biome, w int32, kind PlatformKind) {
    switch kind {
    case PlatformKind.CloudP {
        stampHRow(tiles, ox, oy, w,
            t(TileKind.CloudLeft, biome), t(TileKind.CloudMiddle, biome), t(TileKind.CloudRight, biome), t(TileKind.Cloud, biome))
    }
    case PlatformKind.Ledge {
        stampHRow(tiles, ox, oy, w,
            t(TileKind.HorizontalLeft, biome), t(TileKind.HorizontalMiddle, biome), t(TileKind.HorizontalRight, biome), t(TileKind.HorizontalMiddle, biome))
    }
    default {
        stampHRow(tiles, ox, oy, w,
            t(TileKind.HorizontalOverhangLeft, biome), t(TileKind.HorizontalMiddle, biome), t(TileKind.HorizontalOverhangRight, biome), t(TileKind.HorizontalMiddle, biome))
    }
    }
}

// ── Platform planning (WorldGen.Platform.plan) ───────────────

func pickKind(rng Random) PlatformKind {
    var n = rng.Next(3)
    switch n {
    case 0 { return PlatformKind.CloudP }
    case 1 { return PlatformKind.Ledge }
    default { return PlatformKind.Overhang }
    }
}

func hasTileAt(tiles Dictionary[int32, TileV], x int32, y int32) bool {
    if x < 0 || x >= chunkCells() || y < 0 {
        return false
    }
    return tiles.ContainsKey(chunkTileKey(x, y))
}

func planPlatforms(rng Random, cfg PlatformConfig, worldSeed int32, biomeScale float32, originTileX int32, floorY int32, ceilingY int32, tiles Dictionary[int32, TileV]) {
    var specs = List[PlatformSpecV]()
    var target = rng.Next(cfg.MinCount, cfg.MaxCount + 1)
    var layerY = floorY - rng.Next(cfg.MinClearance, cfg.MaxClearance + 1)

    while specs.Count < target && layerY >= ceilingY {
        var maxTries = rng.Next(3, 7)
        for var attempt = 0; attempt < maxTries; attempt++ {
            // No target check here — the F# original has a deliberate no-op
            // (it keeps drawing rng and may overshoot the target within a
            // layer); parity requires the same rng consumption.
            var w = rng.Next(cfg.MinWidth, cfg.MaxWidth + 1)
            var hi = chunkCells() - w
            if hi < 1 { hi = 1 }
            var x = rng.Next(0, hi)

            if layerY >= ceilingY && layerY < floorY && x + w <= chunkCells() {
                // All platform cells must be free.
                var cellsOk = true
                for var ci = 0; ci < w; ci++ {
                    if hasTileAt(tiles, x + ci, layerY) {
                        cellsOk = false
                        break
                    }
                }

                // Clearance: ground must not sit closer than MinClearance below.
                var clearanceOk = cellsOk
                for var cci = 0; cci < w; cci++ {
                    var groundFound = false
                    var sy = layerY + 1
                    while !groundFound && sy <= layerY + cfg.MaxClearance {
                        if hasTileAt(tiles, x + cci, sy) {
                            groundFound = true
                        } else {
                            sy = sy + 1
                        }
                    }
                    if groundFound && (sy - layerY) < cfg.MinClearance {
                        clearanceOk = false
                    }
                }

                // Vertical spacing + X non-overlap vs placed specs.
                var spacingOk = true
                for var si = 0; si < specs.Count; si++ {
                    var s = specs[si]
                    var xTooClose = x < s.X + s.W + 1 && s.X < x + w + 1
                    var dy = s.Y - layerY
                    if dy < 0 { dy = -dy }
                    var yTooClose = dy < cfg.MinVerticalGap
                    if xTooClose && yTooClose {
                        spacingOk = false
                        break
                    }
                }

                if cellsOk && clearanceOk && spacingOk {
                    var biome = biomeAtColumn(originTileX + x, worldSeed, biomeScale)
                    var spec = PlatformSpecV{X: x, Y: layerY, W: w, Kind: pickKind(rng)}
                    specs.Add(spec)
                    stampPlatform(tiles, spec.X, spec.Y, biome, spec.W, spec.Kind)
                }
            }
        }
        layerY = layerY - rng.Next(cfg.MinVerticalGap, cfg.MaxVerticalGap + 1)
    }
}

// ── Extraction (WorldGen.extractAll) ─────────────────────────

func cellTile(tiles Dictionary[int32, TileV], x int32, y int32) TileV {
    return tiles[chunkTileKey(x, y)]
}

// Edge is exposed when the neighbor is absent or not one-way.
func edgeExposed(tiles Dictionary[int32, TileV], nx int32, ny int32) bool {
    if !hasTileAt(tiles, nx, ny) {
        return true
    }
    return !isOneWay(cellTile(tiles, nx, ny).Kind)
}

// Single pass over the chunk grid: collect colliders, hazards, collectibles,
// occluders, and torch lights.
func extractAll(tiles Dictionary[int32, TileV], originX float32, originY float32, rng Random, chunkBiome Biome) ChunkV {
    var platforms = List[RectV](256)
    var oneWayPlatforms = List[RectV](64)
    var spikes = List[RectV](32)
    var coins = List[RectV](64)
    var flags = List[RectV](4)
    var occluders = List[OccluderV](maxOccluders())
    var torches = List[TorchLight](maxTorchLights())

    var cellW = tileSize()
    var cellH = tileSize()

    for var y = 0; y < chunkCells(); y++ {
        for var x = 0; x < chunkCells(); x++ {
            if !tiles.ContainsKey(chunkTileKey(x, y)) {
                continue
            }
            var tile = cellTile(tiles, x, y)
            var wx = originX + float32(x) * cellW
            var wy = originY + float32(y) * cellH
            var solid = isSolid(tile.Kind)
            var oneway = isOneWay(tile.Kind)

            if solid {
                var cr = tileInfo(tile.Kind, tile.Biome).ColliderRect
                platforms.Add(RectV{X: wx + cr.X, Y: wy + cr.Y, W: cr.W, H: cr.H})
            } else if oneway {
                var cr = tileInfo(tile.Kind, tile.Biome).ColliderRect
                oneWayPlatforms.Add(RectV{X: wx + cr.X, Y: wy + cr.Y, W: cr.W, H: cr.H})
            }

            if (solid || oneway) && torches.Count < maxTorchLights() {
                // A torch stands where the cell above is free.
                if !hasTileAt(tiles, x, y - 1) && rng.NextDouble() > 0.92 {
                    torches.Add(TorchLight{
                        Position: Vector2(wx + cellW * 0.5f, wy - 10f),
                        Color: Mibo.Color(byte(255), byte(160), byte(60), byte(255)),
                        Radius: 100f + float32(rng.Next(-20, 20))
                    })
                }
            }

            if isHazard(tile.Kind) {
                spikes.Add(RectV{X: wx, Y: wy, W: cellW, H: cellH})
            }

            if tile.Kind == TileKind.Coin {
                coins.Add(RectV{X: wx, Y: wy, W: cellW, H: cellH})
            }

            if tile.Kind == TileKind.Flag {
                flags.Add(RectV{X: wx, Y: wy, W: cellW, H: cellH})
            }

            if oneway && occluders.Count < maxOccluders() {
                if edgeExposed(tiles, x, y + 1) {
                    occluders.Add(OccluderV{P1: Vector2(wx, wy + cellH), P2: Vector2(wx + cellW, wy + cellH)})
                }
                if occluders.Count < maxOccluders() && edgeExposed(tiles, x - 1, y) {
                    occluders.Add(OccluderV{P1: Vector2(wx, wy), P2: Vector2(wx, wy + cellH)})
                }
                if occluders.Count < maxOccluders() && edgeExposed(tiles, x + 1, y) {
                    occluders.Add(OccluderV{P1: Vector2(wx + cellW, wy), P2: Vector2(wx + cellW, wy + cellH)})
                }
            }
        }
    }

    return ChunkV{
        Tiles: tiles,
        OriginX: originX,
        OriginY: originY,
        Platforms: platforms.ToArray(),
        OneWayPlatforms: oneWayPlatforms.ToArray(),
        Spikes: spikes.ToArray(),
        Coins: coins.ToArray(),
        Flags: flags.ToArray(),
        Occluders: occluders.ToArray(),
        Torches: torches.ToArray(),
        Biome: chunkBiome
    }
}

// ── Chunk generation (WorldGen.generateChunk) ────────────────

func chunkDictKey(cx int32, cy int32) int64 {
    return (int64(cy) << 32) | (int64(cx) & 0xFFFFFFFF)
}

func generateChunk(cx int32, cy int32, worldSeed int32) ChunkV {
    var cfg = defaultConfig()
    var rng = Random(chunkSeed(cx, cy, worldSeed))

    // World-tile-X of this chunk's leftmost column.
    var originTileX = cx * chunkCells()

    // Next chunk's first slab Y (deterministic, for cross-seam clamping).
    var nextFirstSlabY = elevationAtColumn(originTileX + chunkCells(), worldSeed, cfg.ElevationScale, cfg.ElevationAmplitude)

    // 1. Plan ground slabs; 2. clamp cross-seam; 3. stamp (biome per slab).
    var elevationForColumn = Func[int32, int32]((lx int32) -> {
        return elevationAtColumn(originTileX + lx, worldSeed, cfg.ElevationScale, cfg.ElevationAmplitude)
    })
    var groundSpecs = planGround(rng, cfg.Ground, maxJumpTiles(), chunkCells(), elevationForColumn)
    groundSpecs = clampCrossSeam(groundSpecs, chunkCells(), nextFirstSlabY)

    var tiles = Dictionary[int32, TileV]()
    for var i = 0; i < groundSpecs.Length; i++ {
        var spec = groundSpecs[i]
        var biome = biomeAtColumn(originTileX + spec.X, worldSeed, cfg.BiomeColumnScale)
        stampGround(tiles, spec.X, spec.Y, biome, spec.W, spec.H)
    }

    // 4. Floating platforms (grid is the occupancy source of truth).
    planPlatforms(rng, cfg.Platform, worldSeed, cfg.BiomeColumnScale, originTileX, groundYTile(), skyCeiling(), tiles)

    // 5. Extract colliders / collectibles / lights.
    var chunkBiome = biomeAtColumn(originTileX, worldSeed, cfg.BiomeColumnScale)
    return extractAll(tiles, float32(cx) * chunkWorldSize(), float32(cy) * chunkWorldSize(), rng, chunkBiome)
}

// ── Chunk streaming (WorldGen.Chunks) ────────────────────────

// Generate up to `budget` missing chunks around the player; evict far ones.
// The F# sample generated asynchronously via Cmd.ofAsync; the adaptive port
// generates synchronously with a per-step budget (cold path, ~1ms/chunk).
func streamChunks(w World) {
    var pcx = int32(MathF.Floor(w.Player.Position.X / chunkWorldSize()))
    var pcy = int32(MathF.Floor(w.Player.Position.Y / chunkWorldSize()))

    var generated int32 = 0
    var r int32 = chunkLoadRadius()
    for var dy = -r; dy <= r; dy++ {
        for var dx = -r; dx <= r; dx++ {
            var key = chunkDictKey(pcx + dx, pcy + dy)
            if !w.Chunks.ContainsKey(key) {
                w.Chunks[key] = generateChunk(pcx + dx, pcy + dy, w.Seed)
                generated = generated + 1
                if generated >= 2 {
                    // finish eviction even when the gen budget is spent
                    dx = r
                    dy = r
                }
            }
        }
    }

    var keysToRemove = List[int64](32)
    for key in w.Chunks.Keys {
        var cy = int32(key >> 32)
        var cx = int32(key & 0xFFFFFFFF)
        var adx = cx - pcx
        if adx < 0 { adx = -adx }
        var ady = cy - pcy
        if ady < 0 { ady = -ady }
        if adx > chunkEvictRadius() || ady > chunkEvictRadius() {
            keysToRemove.Add(key)
        }
    }
    for key in keysToRemove {
        w.Chunks.Remove(key)
    }
}
