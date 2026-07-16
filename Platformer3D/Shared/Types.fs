module Platformer3D.Types

open System.Numerics
open Mibo.Layout3D
open Platformer3D.Constants

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveForward
  | MoveBackward
  | Jump
  | Respawn
  | RotateCameraLeft
  | RotateCameraRight
  | RotateCameraUp
  | RotateCameraDown

[<Struct>]
type BlockType =
  | Empty
  | Ground
  | GroundSlopeXPos
  | GroundSlopeXNeg
  | GroundSlopeZPos
  | GroundSlopeZNeg
  | Platform
  | PlatformRamp
  | SnowGround
  | SnowSlopeXPos
  | SnowSlopeXNeg
  | SnowSlopeZPos
  | SnowSlopeZNeg
  | Spikes
  | TreePine
  | TreeSnow
  | Rock
  | GrassTuft
  | Coin
  | Jewel
  | Heart
  | Star
  | Mushrooms
  | Crate
  | Barrel
  | Flag
  | MushroomLight

module BlockType =
  /// Bare logical model name (backend composes basePath + extension).
  let modelName =
    function
    | Ground -> KenneyModels.blockGrass
    | GroundSlopeXPos -> KenneyModels.blockGrassSlope
    | GroundSlopeXNeg -> KenneyModels.blockGrassSlope
    | GroundSlopeZPos -> KenneyModels.blockGrassSlope
    | GroundSlopeZNeg -> KenneyModels.blockGrassSlope
    | Platform -> KenneyModels.platform
    | PlatformRamp -> KenneyModels.platformRamp
    | SnowGround -> KenneyModels.blockSnow
    | SnowSlopeXPos -> KenneyModels.blockSnowSlope
    | SnowSlopeXNeg -> KenneyModels.blockSnowSlope
    | SnowSlopeZPos -> KenneyModels.blockSnowSlope
    | SnowSlopeZNeg -> KenneyModels.blockSnowSlope
    | Spikes -> KenneyModels.spikeBlock
    | TreePine -> KenneyModels.treePine
    | TreeSnow -> KenneyModels.treeSnow
    | Rock -> KenneyModels.rocks
    | GrassTuft -> KenneyModels.grass
    | Coin -> KenneyModels.coinGold
    | Jewel -> KenneyModels.jewel
    | Heart -> KenneyModels.heart
    | Star -> KenneyModels.star
    | Mushrooms -> KenneyModels.mushrooms
    | Crate -> KenneyModels.crate
    | Barrel -> KenneyModels.barrel
    | Flag -> KenneyModels.flag
    | MushroomLight -> KenneyModels.mushrooms
    | Empty -> ""

  let modelVerticalOffset =
    function
    | Platform
    | PlatformRamp -> cellSize * 0.5f
    | Coin
    | Jewel
    | Heart
    | Star
    | Flag -> cellSize * 0.5f
    | _ -> 0.0f

  let modelRotation =
    function
    | GroundSlopeXNeg -> 180.0f
    | GroundSlopeZPos -> 90.0f
    | GroundSlopeZNeg -> -90.0f
    | SnowSlopeXNeg -> 180.0f
    | SnowSlopeZPos -> 90.0f
    | SnowSlopeZNeg -> -90.0f
    | _ -> 0.0f

  let isSolid =
    function
    | Empty
    | Coin
    | Jewel
    | Heart
    | Star
    | GrassTuft
    | Mushrooms
    | MushroomLight
    | Flag -> false
    | _ -> true

  let isCollectible =
    function
    | Coin
    | Jewel
    | Heart
    | Star -> true
    | _ -> false

  let isDecoration =
    function
    | TreePine
    | TreeSnow
    | Rock
    | GrassTuft
    | Mushrooms
    | Flag
    | Barrel
    | Crate -> true
    | _ -> false

  let isLightSource =
    function
    | MushroomLight -> true
    | _ -> false

[<Struct>]
type Chunk = {
  Grid: CellGrid3D<BlockType>
  Bounds: BoundingBox
  OriginX: int
  OriginZ: int
}
