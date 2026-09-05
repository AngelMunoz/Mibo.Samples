module Platformer3D.Raylib.Types

open System.Collections.Generic
open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Input
open Platformer3D.Types
open Platformer3D.Constants

// -------------------------------------------------------------
// Asset path composition (backend-specific)
// -------------------------------------------------------------

module AssetPaths =
  let modelBasePath = "assets/kenney_platformer-kit/Models/"

  let modelPath name = modelBasePath + name + ".glb"

// A static prop parented to a skeleton bone at draw time (bone-attachment demo).
type AttachedProp = {
  BoneName: string
  LocalTransform: Matrix4x4
  Mesh: Mesh
  Material: Material3D
}

// Instanced oozi crowd (skinned-instancing probe): one shared AnimatedMesh and
// clip set, one playback state per instance. The View refills Transforms around
// the player each frame, evaluates one pose per instance into the reused Poses
// array, and issues a single animatedModelInstanced draw for the whole ring.
// See Platformer3D.Constants.OoziCrowd for the shared tuning knobs.
type OoziCrowd = {
  AnimMesh: AnimatedMesh voption
  States: Animation3DState[]
  Transforms: Matrix4x4[]
  Poses: BonePose[]
}

// -------------------------------------------------------------
// Root Model — composes shared sub-system models + backend-specific state
// -------------------------------------------------------------

type Model() =
  // Shared sub-system models
  member val Physics = Platformer3D.Physics.PhysicsSystem.init() with get, set

  member val Chunks = Platformer3D.WorldGen.Chunks.init 0 with get, set
  member val Particles = Platformer3D.Particles.init() with get, set

  member val DayNight =
    Platformer3D.DayNight.DayNightSystem.init() with get, set

  member val Lighting =
    Platformer3D.Lighting.LightingSystem.init() with get, set

  member val Minimap = Platformer3D.MinimapSystem.init() with get, set
  member val Diag = Platformer3D.Diagnostics.init() with get, set
  // Input
  member val Actions: ActionState<GameAction> = ActionState.empty with get, set

  member val InputMap: InputMap<GameAction> = InputMap.empty with get, set
  // Backend-specific state
  member val PlayerModel = Unchecked.defaultof<Raylib_cs.Model> with get, set

  member val PlayerAnimClips =
    Unchecked.defaultof<Animation3DClips> with get, set

  member val PlayerAnim = Unchecked.defaultof<Animation3DState> with get, set

  // Bone-attachment demo: shared GPU-skinning mesh for the player plus weapons
  // parented to the player's handslot sockets (sword right, wand left).
  member val PlayerAnimatedMesh: AnimatedMesh voption = ValueNone with get, set

  member val PlayerProps: AttachedProp[] = Array.empty with get, set

  // Skinned-instancing probe: the oozi ring drawn through one
  // animatedModelInstanced call per frame (ValueNone until init loads it).
  member val Oozi: OoziCrowd voption = ValueNone with get, set

  member val ModelCache = Dictionary<string, Raylib_cs.Model>() with get, set
  member val VisibleLights = ResizeArray<PointLight3D>() with get, set
  member val ParticleTexture = Unchecked.defaultof<Texture2D> with get, set
  member val MinimapTexture = Unchecked.defaultof<Texture2D> with get, set

  member val MinimapTexReady = false with get, set

  member val DiagFont = Raylib.GetFontDefault() with get, set

// -------------------------------------------------------------
// Root Msg
// -------------------------------------------------------------

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputMapped of inputs: ActionState<GameAction>
  | ChunkCreated of key: struct (int * int) * chunk: Chunk
  | MinimapReady of colors: Mibo.Color[] * width: int * height: int
  | MushroomLightsReady of lights: PointLight3D[]
