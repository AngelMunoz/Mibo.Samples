module Platformer3D.Constants

open System.Numerics

[<Literal>]
let cellSize = 1.0f

[<Literal>]
let chunkWidth = 32

[<Literal>]
let chunkHeight = 16

[<Literal>]
let chunkDepth = 32

let chunkWorldWidth = float32 chunkWidth * cellSize

let chunkWorldDepth = float32 chunkDepth * cellSize

[<Literal>]
let playerHeight = 1.8f

[<Literal>]
let playerRadius = 0.21f

[<Literal>]
let gravity = -20.0f

[<Literal>]
let jumpSpeed = 12.0f

[<Literal>]
let moveSpeed = 8.0f

[<Literal>]
let acceleration = 25.0f

[<Literal>]
let friction = 8.0f

[<Literal>]
let fallLimit = -30.0f

[<Literal>]
let cameraDistance = 8.0f

[<Literal>]
let cameraHeightOffset = 2.0f

[<Literal>]
let cameraLerpSpeed = 10.0f

[<Literal>]
let cameraDefaultPitch = 0.15f

[<Literal>]
let cameraDefaultYaw = System.MathF.PI / 4.0f

[<Literal>]
let mouseSensitivity = 0.003f

[<Literal>]
let viewportWidth = 1280.0f

[<Literal>]
let viewportHeight = 720.0f

[<Literal>]
let chunkLoadRadius = 1

[<Literal>]
let chunkEvictRadius = 4

let spawnPosition =
  Vector3(float32 chunkWidth / 2.0f, 10.0f, float32 chunkDepth / 2.0f)

let arcRadius = float32 chunkLoadRadius * chunkWorldWidth * 5.0f

// ── Bare logical model names (backend composes basePath + extension) ──

module KenneyModels =
  let blockGrass = "block-grass"
  let blockGrassLarge = "block-grass-large"
  let blockGrassTall = "block-grass-large-tall"
  let blockGrassLong = "block-grass-long"
  let blockGrassLow = "block-grass-low"
  let blockGrassSlope = "block-grass-large-slope"
  let blockGrassSlopeSteep = "block-grass-large-slope-steep"
  let blockGrassNarrow = "block-grass-narrow"
  let blockGrassEdge = "block-grass-edge"
  let blockGrassCorner = "block-grass-corner"

  let blockSnow = "block-snow"
  let blockSnowLarge = "block-snow-large"
  let blockSnowTall = "block-snow-large-tall"
  let blockSnowLong = "block-snow-long"
  let blockSnowLow = "block-snow-low"
  let blockSnowSlope = "block-snow-large-slope"
  let blockSnowNarrow = "block-snow-narrow"

  let platform = "platform"
  let platformFortified = "platform-fortified"
  let platformRamp = "platform-ramp"
  let platformOverhang = "platform-overhang"

  let characterOobi = "character-oobi"
  let characterOodi = "character-oodi"
  let characterOoli = "character-ooli"
  let characterOopi = "character-oopi"
  let characterOozi = "character-oozi"

  let coinGold = "coin-gold"
  let coinSilver = "coin-silver"
  let coinBronze = "coin-bronze"
  let jewel = "jewel"
  let heart = "heart"
  let star = "star"
  let key = "key"

  let spikeBlock = "spike-block"
  let spikeBlockWide = "spike-block-wide"
  let trapSpikes = "trap-spikes"
  let trapSpikesLarge = "trap-spikes-large"
  let saw = "saw"

  let treePine = "tree-pine"
  let treePineSmall = "tree-pine-small"
  let treeSnow = "tree-snow"
  let rocks = "rocks"
  let stones = "stones"
  let grass = "grass"
  let flowers = "flowers"
  let flowersTall = "flowers-tall"
  let mushrooms = "mushrooms"

  let fenceStraight = "fence-straight"
  let fenceCorner = "fence-corner"
  let fenceRope = "fence-rope"

  let crate = "crate"
  let barrel = "barrel"
  let chest = "chest"

  let ladder = "ladder"
  let ladderLong = "ladder-long"

  let sign = "sign"
  let flag = "flag"
  let arrow = "arrow"

  let bomb = "bomb"
  let spring = "spring"

  let doorOpen = "door-open"
  let doorRotate = "door-rotate"

  let brick = "brick"
  let pipe = "pipe"
  let poles = "poles"
  let conveyorBelt = "conveyor-belt"
  let plant = "plant"
  let hedge = "hedge"
  let hedgeCorner = "hedge-corner"
