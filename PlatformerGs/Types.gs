// file: Types.gs
// Core types + constants for the PlatformerGs port (Adaptive architecture).
// Ported from Platformer/Shared (Constants.fs, Types.fs, DayNight.fs) with
// Mibo.Layout's LayeredGrid2D replaced by a plain per-chunk tile dictionary.

package PlatformerGs

import System
import System.Numerics
import System.Collections.Generic
import Mibo
import Mibo.Animation
import Mibo.Elmish.Graphics2D.Lighting
import Raylib_cs

// ── Constants (Platformer/Shared/Constants.fs) ───────────────

func tileSize() float32 { return 64f }
func chunkCells() int32 { return 32 }
func chunkWorldSize() float32 { return float32(chunkCells()) * tileSize() }
func playerWidth() float32 { return 40f }
func playerHeight() float32 { return 54f }
func gravity() float32 { return 2000f }
func moveSpeed() float32 { return 350f }
func jumpSpeed() float32 { return -1100f }
func jumpCutMultiplier() float32 { return 0.25f }
func worldHeight() float32 { return 10f }
func groundLevel() float32 { return worldHeight() * tileSize() }
func groundSurface() float32 { return groundLevel() - tileSize() }
func chunkLoadRadius() int32 { return 3 }
func chunkEvictRadius() int32 { return 5 }
func maxOccluders() int32 { return 128 }
func maxTorchLights() int32 { return 16 }
func viewportWidth() int32 { return 1280 }
func viewportHeight() int32 { return 720 }
func spawnX() float32 { return 200f }
func spawnProtectedCells() int32 { return 5 }

// ── Enums ────────────────────────────────────────────────────

enum Biome {
    Grass, Dirt, Stone, Snow, Sand, Purple
}

enum TileKind {
    Empty, Block, BlockTop, BlockBottom, BlockTopLeft, BlockTopRight,
    BlockBottomLeft, BlockBottomRight, BlockLeft, BlockRight, BlockCenter,
    HorizontalLeft, HorizontalMiddle, HorizontalOverhangLeft,
    HorizontalOverhangRight, HorizontalRight,
    VerticalBottom, VerticalMiddle, VerticalTop,
    RampLongA, RampLongB, RampLongC, RampShortA, RampShortB,
    Cloud, CloudBackground, CloudLeft, CloudMiddle, CloudRight,
    Bridge, BridgeLogs,
    Spikes, BlockSpikes, Lava, LavaTop, LavaTopLow,
    Coin, Flag
}

enum ColliderKind {
    NoneK, FullBlock, OneWay, Hazard
}

enum PlatformKind {
    CloudP, Ledge, Overhang
}

enum AnimState {
    Idle, Walk, Jump, Fall, Duck
}

// ── Value structs ────────────────────────────────────────────

data struct RectV {
    var X float32
    var Y float32
    var W float32
    var H float32
}

data struct TileV {
    var Kind TileKind
    var Biome Biome
}

data struct TileInfo {
    var SpriteX float32
    var SpriteY float32
    var Collider ColliderKind
    var ColliderRect RectV
}

data struct OccluderV {
    var P1 Vector2
    var P2 Vector2
}

data struct TorchLight {
    var Position Vector2
    var Color Mibo.Color
    var Radius float32
}

data struct ParticleV {
    var Position Vector2
    var Size Vector2
    var Rotation float32
    var Color Mibo.Color
}

// ── Gen config (WorldGen.fs) ─────────────────────────────────

data struct GroundConfig {
    var MinSlabs int32
    var MaxSlabs int32
    var MinWidth int32
    var MaxWidth int32
    var MinHeight int32
    var MaxHeight int32
    var MinGap int32
    var MaxGap int32
}

data struct PlatformConfig {
    var MinCount int32
    var MaxCount int32
    var MinWidth int32
    var MaxWidth int32
    var MinClearance int32
    var MaxClearance int32
    var MinVerticalGap int32
    var MaxVerticalGap int32
}

data struct GenConfig {
    var Ground GroundConfig
    var Platform PlatformConfig
    var BiomeColumnScale float32
    var ElevationScale float32
    var ElevationAmplitude int32
}

data struct GroundSpec {
    var X int32
    var Y int32
    var W int32
    var H int32
}

data struct PlatformSpecV {
    var X int32
    var Y int32
    var W int32
    var Kind PlatformKind
}

// ── Chunk ────────────────────────────────────────────────────

// One chunk of generated world. The tile grid is a flat dictionary keyed by
// cellY * chunkCells + cellX (only stamped cells exist — absent = Empty).
data struct ChunkV {
    var Tiles Dictionary[int32, TileV]
    var OriginX float32
    var OriginY float32
    var Platforms []RectV
    var OneWayPlatforms []RectV
    var Spikes []RectV
    var Coins []RectV
    var Flags []RectV
    var Occluders []OccluderV
    var Torches []TorchLight
    var Biome Biome
}

func chunkTileKey(x int32, y int32) int32 {
    return y * chunkCells() + x
}

// ── Day/Night (DayNight.fs) ──────────────────────────────────

data struct DayNightState {
    var TimeOfDay float32
    var TotalTime float32
}

// ── Assets (raylib-direct; loaded once at init) ──────────────

class SpriteAssets {
    var PlayerSheet SpriteSheet
    var TileTexture Texture2D
    var TorchSheet SpriteSheet
    var TileEffectSheet SpriteSheet
    var ParticleTexture Texture2D
    var Font Font
    var JumpSound Sound
}

// ── Root world state (the adaptive program's mutable state) ──

class PlayerState {
    var Position Vector2
    var Velocity Vector2
    var Facing float32 = 1f
    var IsGrounded bool = true
    var JumpTriggered bool
    var IsDucking bool
    var Score int32
}

class World {
    var Seed int32
    var Assets SpriteAssets
    var Player PlayerState
    var Chunks Dictionary[int64, ChunkV]
    var Particles []ParticleV
    var ParticleVelocities []Vector2
    var ParticleCount int32
    var DayNight DayNightState
    var Camera2D Camera2D
    var Lighting LightContext2D
    var PlayerSprite AnimatedSprite
    var TorchSprite AnimatedSprite
    var CoinSprite AnimatedSprite
    var FlagSprite AnimatedSprite
    var TimeOfDay float32 = 12f
    // input edges
    var PrevJumpHeld bool
    var PrevRespawnHeld bool
}

// Package-level constructor (G# classes have no static members).
func createWorld(seed int32) World {
    var w = World()
    w.Seed = seed
    w.Player = PlayerState()
    w.Player.Position = Vector2(spawnX(), groundSurface() - playerHeight())
    w.Chunks = Dictionary[int64, ChunkV]()
    w.Particles = [512]ParticleV
    w.ParticleVelocities = [512]Vector2
    w.DayNight = DayNightState{TimeOfDay: 12f, TotalTime: 0f}
    return w
}

// ── RenderFrame — packed once per Step, read by the renderer ─

data struct RenderFrame {
    var World World
    var Frame int64
}
