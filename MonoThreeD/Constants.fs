module MonoThreeD.Constants

open Microsoft.Xna.Framework

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
let playerRadius = 0.3f

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

module KenneyModels =
  // MonoGame content root is "Content/"; asset paths omit the extension and the
  // "assets/" prefix the Raylib sample used. Models are built by the content
  // pipeline (Content.mgcb) to kenney_platformer-kit/Models/<name>.
  let basePath = "kenney_platformer-kit/Models/"

  let blockGrass = basePath + "block-grass"
  let blockGrassLarge = basePath + "block-grass-large"
  let blockGrassTall = basePath + "block-grass-large-tall"
  let blockGrassSlope = basePath + "block-grass-large-slope"
  let blockGrassSlopeSteep = basePath + "block-grass-large-slope-steep"
  let blockGrassNarrow = basePath + "block-grass-narrow"
  let blockGrassEdge = basePath + "block-grass-edge"
  let blockGrassCorner = basePath + "block-grass-corner"

  let blockSnow = basePath + "block-snow"
  let blockSnowLarge = basePath + "block-snow-large"
  let blockSnowTall = basePath + "block-snow-large-tall"
  let blockSnowSlope = basePath + "block-snow-large-slope"

  let platform = basePath + "platform"
  let platformFortified = basePath + "platform-fortified"
  let platformRamp = basePath + "platform-ramp"
  let platformOverhang = basePath + "platform-overhang"

  let characterOobi = basePath + "character-oobi"
  let characterOodi = basePath + "character-oodi"
  let characterOoli = basePath + "character-ooli"
  let characterOopi = basePath + "character-oopi"
  let characterOozi = basePath + "character-oozi"

  let coinGold = basePath + "coin-gold"
  let coinSilver = basePath + "coin-silver"
  let coinBronze = basePath + "coin-bronze"
  let jewel = basePath + "jewel"
  let heart = basePath + "heart"
  let star = basePath + "star"
  let key = basePath + "key"

  let spikeBlock = basePath + "spike-block"
  let spikeBlockWide = basePath + "spike-block-wide"
  let trapSpikes = basePath + "trap-spikes"
  let trapSpikesLarge = basePath + "trap-spikes-large"
  let saw = basePath + "saw"

  let treePine = basePath + "tree-pine"
  let treePineSmall = basePath + "tree-pine-small"
  let treeSnow = basePath + "tree-snow"
  let rocks = basePath + "rocks"
  let stones = basePath + "stones"
  let grass = basePath + "grass"
  let flowers = basePath + "flowers"
  let flowersTall = basePath + "flowers-tall"
  let mushrooms = basePath + "mushrooms"

  let fenceStraight = basePath + "fence-straight"
  let fenceCorner = basePath + "fence-corner"
  let fenceRope = basePath + "fence-rope"

  let crate = basePath + "crate"
  let barrel = basePath + "barrel"
  let chest = basePath + "chest"

  let ladder = basePath + "ladder"
  let ladderLong = basePath + "ladder-long"

  let sign = basePath + "sign"
  let flag = basePath + "flag"
  let arrow = basePath + "arrow"

  let bomb = basePath + "bomb"
  let spring = basePath + "spring"

  let doorOpen = basePath + "door-open"
  let doorRotate = basePath + "door-rotate"

  let brick = basePath + "brick"
  let pipe = basePath + "pipe"
  let poles = basePath + "poles"
  let conveyorBelt = basePath + "conveyor-belt"
  let plant = basePath + "plant"
  let hedge = basePath + "hedge"
  let hedgeCorner = basePath + "hedge-corner"
