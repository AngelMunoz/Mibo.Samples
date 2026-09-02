// file: Tiles.gs
// Tile metadata registry — sprite source rects + colliders per tile.
// Ported from Platformer/Shared/TileData.fs (Kenney atlas positions).

package PlatformerGs

import System
import Raylib_cs

// Pixel positions for each biome's 28-tile group in the atlas. Two parallel
// arrays (xs, ys) — index = the tile group order documented in TileData.fs:
// 0=block 1=block_bottom 2=block_bottom_left 3=block_bottom_right
// 4=block_center 5=block_left 6=block_right 7=block_top 8=block_top_left
// 9=block_top_right 10=cloud 11=cloud_background 12=cloud_left 13=cloud_middle
// 14=cloud_right 15=horizontal_left 16=horizontal_middle 17=overhang_left
// 18=overhang_right 19=horizontal_right 20=ramp_long_a 21=ramp_long_b
// 22=ramp_long_c 23=ramp_short_a 24=ramp_short_b 25=vertical_bottom
// 26=vertical_middle 27=vertical_top

func grassXs() []float32 {
    return []float32{260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f}
}
func grassYs() []float32 {
    var ys = [28]float32
    for var i = 0; i < 14; i++ { ys[i] = 585f }
    for var i = 14; i < 28; i++ { ys[i] = 650f }
    return ys
}
func dirtXs() []float32 {
    return []float32{780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f}
}
func dirtYs() []float32 {
    var ys = [28]float32
    for var i = 0; i < 6; i++ { ys[i] = 455f }
    for var i = 6; i < 24; i++ { ys[i] = 520f }
    for var i = 24; i < 28; i++ { ys[i] = 585f }
    return ys
}
func sandXs() []float32 {
    return []float32{390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f}
}
func sandYs() []float32 {
    var ys = [28]float32
    for var i = 0; i < 12; i++ { ys[i] = 780f }
    for var i = 12; i < 28; i++ { ys[i] = 845f }
    return ys
}
func snowXs() []float32 {
    return []float32{1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f}
}
func snowYs() []float32 {
    var ys = [28]float32
    for var i = 0; i < 10; i++ { ys[i] = 845f }
    for var i = 10; i < 20; i++ { ys[i] = 910f }
    for var i = 20; i < 28; i++ { ys[i] = 975f }
    return ys
}
func stoneXs() []float32 {
    return []float32{520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f}
}
func stoneYs() []float32 {
    var ys = [28]float32
    for var i = 0; i < 10; i++ { ys[i] = 975f }
    for var i = 10; i < 28; i++ { ys[i] = 1040f }
    return ys
}
func purpleXs() []float32 {
    return []float32{910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f, 390f, 455f, 520f, 585f, 650f, 715f, 780f, 845f, 910f, 975f, 1040f, 1105f, 0f, 65f, 130f, 195f, 260f, 325f}
}
func purpleYs() []float32 {
    var ys = [28]float32
    for var i = 0; i < 10; i++ { ys[i] = 650f }
    for var i = 10; i < 22; i++ { ys[i] = 715f }
    for var i = 22; i < 28; i++ { ys[i] = 780f }
    return ys
}

func biomeXs(biome Biome) []float32 {
    switch biome {
    case Biome.Grass { return grassXs() }
    case Biome.Dirt { return dirtXs() }
    case Biome.Sand { return sandXs() }
    case Biome.Snow { return snowXs() }
    case Biome.Stone { return stoneXs() }
    default { return purpleXs() }
    }
}

func biomeYs(biome Biome) []float32 {
    switch biome {
    case Biome.Grass { return grassYs() }
    case Biome.Dirt { return dirtYs() }
    case Biome.Sand { return sandYs() }
    case Biome.Snow { return snowYs() }
    case Biome.Stone { return stoneYs() }
    default { return purpleYs() }
    }
}

func fullRect() RectV {
    return RectV{X: 0f, Y: 0f, W: 64f, H: 64f}
}

func infoAt(biome Biome, idx int32, collider ColliderKind, rect RectV) TileInfo {
    return TileInfo{
        SpriteX: biomeXs(biome)[idx],
        SpriteY: biomeYs(biome)[idx],
        Collider: collider,
        ColliderRect: rect
    }
}

func rawInfo(x float32, y float32, collider ColliderKind, rect RectV) TileInfo {
    return TileInfo{SpriteX: x, SpriteY: y, Collider: collider, ColliderRect: rect}
}

/// Sprite source rect + collider for a tile (TileData.lookup).
func tileInfo(kind TileKind, biome Biome) TileInfo {
    switch kind {
    case TileKind.Empty { return rawInfo(0f, 0f, ColliderKind.NoneK, fullRect()) }
    case TileKind.Block { return infoAt(biome, 0, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockBottom { return infoAt(biome, 1, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockBottomLeft { return infoAt(biome, 2, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockBottomRight { return infoAt(biome, 3, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockCenter { return infoAt(biome, 4, ColliderKind.NoneK, fullRect()) }
    case TileKind.BlockLeft { return infoAt(biome, 5, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockRight { return infoAt(biome, 6, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockTop { return infoAt(biome, 7, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockTopLeft { return infoAt(biome, 8, ColliderKind.FullBlock, fullRect()) }
    case TileKind.BlockTopRight { return infoAt(biome, 9, ColliderKind.FullBlock, fullRect()) }
    case TileKind.HorizontalLeft { return infoAt(biome, 15, ColliderKind.FullBlock, fullRect()) }
    case TileKind.HorizontalMiddle { return infoAt(biome, 16, ColliderKind.FullBlock, fullRect()) }
    case TileKind.HorizontalOverhangLeft { return infoAt(biome, 17, ColliderKind.FullBlock, fullRect()) }
    case TileKind.HorizontalOverhangRight { return infoAt(biome, 18, ColliderKind.FullBlock, fullRect()) }
    case TileKind.HorizontalRight { return infoAt(biome, 19, ColliderKind.FullBlock, fullRect()) }
    case TileKind.VerticalBottom { return infoAt(biome, 25, ColliderKind.FullBlock, fullRect()) }
    case TileKind.VerticalMiddle { return infoAt(biome, 26, ColliderKind.FullBlock, fullRect()) }
    case TileKind.VerticalTop { return infoAt(biome, 27, ColliderKind.FullBlock, fullRect()) }
    case TileKind.RampLongA { return infoAt(biome, 20, ColliderKind.FullBlock, fullRect()) }
    case TileKind.RampLongB { return infoAt(biome, 21, ColliderKind.FullBlock, fullRect()) }
    case TileKind.RampLongC { return infoAt(biome, 22, ColliderKind.FullBlock, fullRect()) }
    case TileKind.RampShortA { return infoAt(biome, 23, ColliderKind.FullBlock, fullRect()) }
    case TileKind.RampShortB { return infoAt(biome, 24, ColliderKind.FullBlock, fullRect()) }
    case TileKind.Cloud { return infoAt(biome, 10, ColliderKind.OneWay, RectV{X: 0f, Y: 0f, W: 64f, H: 52f}) }
    case TileKind.CloudBackground { return infoAt(biome, 11, ColliderKind.OneWay, RectV{X: 0f, Y: 8f, W: 64f, H: 46f}) }
    case TileKind.CloudLeft { return infoAt(biome, 12, ColliderKind.OneWay, RectV{X: 0f, Y: 0f, W: 64f, H: 52f}) }
    case TileKind.CloudMiddle { return infoAt(biome, 13, ColliderKind.OneWay, RectV{X: 0f, Y: 0f, W: 64f, H: 52f}) }
    case TileKind.CloudRight { return infoAt(biome, 14, ColliderKind.OneWay, RectV{X: 0f, Y: 0f, W: 64f, H: 52f}) }
    case TileKind.Bridge { return rawInfo(715f, 65f, ColliderKind.OneWay, RectV{X: 0f, Y: 0f, W: 64f, H: 34f}) }
    case TileKind.BridgeLogs { return rawInfo(780f, 65f, ColliderKind.OneWay, RectV{X: 0f, Y: 0f, W: 64f, H: 27f}) }
    case TileKind.Spikes { return rawInfo(0f, 455f, ColliderKind.Hazard, fullRect()) }
    case TileKind.BlockSpikes { return rawInfo(715f, 0f, ColliderKind.Hazard, fullRect()) }
    case TileKind.Lava { return rawInfo(910f, 325f, ColliderKind.Hazard, fullRect()) }
    case TileKind.LavaTop { return rawInfo(975f, 325f, ColliderKind.Hazard, fullRect()) }
    case TileKind.LavaTopLow { return rawInfo(1040f, 325f, ColliderKind.Hazard, RectV{X: 0f, Y: 32f, W: 64f, H: 32f}) }
    case TileKind.Coin { return rawInfo(0f, 130f, ColliderKind.NoneK, fullRect()) }
    default { return rawInfo(1105f, 130f, ColliderKind.NoneK, fullRect()) }
    }
}

// ── Predicates (TileData.fs) ─────────────────────────────────

func isSolid(kind TileKind) bool {
    switch kind {
    case TileKind.Empty { return false }
    case TileKind.Coin { return false }
    case TileKind.Flag { return false }
    case TileKind.Cloud { return false }
    case TileKind.CloudBackground { return false }
    case TileKind.CloudLeft { return false }
    case TileKind.CloudMiddle { return false }
    case TileKind.CloudRight { return false }
    case TileKind.BlockCenter { return false }
    case TileKind.Bridge { return false }
    case TileKind.BridgeLogs { return false }
    case TileKind.Spikes { return false }
    case TileKind.BlockSpikes { return false }
    case TileKind.Lava { return false }
    case TileKind.LavaTop { return false }
    case TileKind.LavaTopLow { return false }
    default { return true }
    }
}

func isOneWay(kind TileKind) bool {
    switch kind {
    case TileKind.Cloud { return true }
    case TileKind.CloudBackground { return true }
    case TileKind.CloudLeft { return true }
    case TileKind.CloudMiddle { return true }
    case TileKind.CloudRight { return true }
    case TileKind.Bridge { return true }
    case TileKind.BridgeLogs { return true }
    default { return false }
    }
}

func isHazard(kind TileKind) bool {
    switch kind {
    case TileKind.Spikes { return true }
    case TileKind.BlockSpikes { return true }
    case TileKind.Lava { return true }
    case TileKind.LavaTop { return true }
    case TileKind.LavaTopLow { return true }
    default { return false }
    }
}

/// Animated tile-effect definitions (TileAnimations.fs) — (name, frames as
/// (x, y) pairs, frameDuration, loop). Converted to SpriteSheet animations
/// at asset-load time.
data struct TileAnimDefV {
    var Name string
    var FrameXs []float32
    var FrameYs []float32
    var FrameDuration float32
    var Loop bool
}

func tileAnimDefs() []TileAnimDefV {
    var defs = [9]TileAnimDefV
    defs[0] = TileAnimDefV{Name: "bomb", FrameXs: []float32{195f, 260f}, FrameYs: []float32{65f, 65f}, FrameDuration: 0.3f, Loop: true}
    defs[1] = TileAnimDefV{Name: "coin_bronze", FrameXs: []float32{1040f, 1105f}, FrameYs: []float32{65f, 65f}, FrameDuration: 0.15f, Loop: true}
    defs[2] = TileAnimDefV{Name: "coin_gold", FrameXs: []float32{0f, 65f}, FrameYs: []float32{130f, 130f}, FrameDuration: 0.15f, Loop: true}
    defs[3] = TileAnimDefV{Name: "coin_silver", FrameXs: []float32{130f, 195f}, FrameYs: []float32{130f, 130f}, FrameDuration: 0.15f, Loop: true}
    defs[4] = TileAnimDefV{Name: "flag_blue", FrameXs: []float32{780f, 845f}, FrameYs: []float32{130f, 130f}, FrameDuration: 0.3f, Loop: true}
    defs[5] = TileAnimDefV{Name: "flag_green", FrameXs: []float32{910f, 975f}, FrameYs: []float32{130f, 130f}, FrameDuration: 0.3f, Loop: true}
    defs[6] = TileAnimDefV{Name: "flag_red", FrameXs: []float32{1105f, 0f}, FrameYs: []float32{130f, 195f}, FrameDuration: 0.3f, Loop: true}
    defs[7] = TileAnimDefV{Name: "flag_yellow", FrameXs: []float32{65f, 130f}, FrameYs: []float32{195f, 195f}, FrameDuration: 0.3f, Loop: true}
    defs[8] = TileAnimDefV{Name: "torch_on", FrameXs: []float32{65f, 130f}, FrameYs: []float32{1105f, 1105f}, FrameDuration: 0.15f, Loop: true}
    return defs
}
