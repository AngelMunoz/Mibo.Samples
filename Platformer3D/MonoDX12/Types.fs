module Platformer3D.MonoGame.Types

open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Audio
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
  let modelBasePath = "kenney_platformer-kit/Models/"

  let modelPath name = modelBasePath + name

// A static prop parented to a skeleton bone at draw time (bone-attachment demo).
type AttachedProp = {
  BoneName: string
  LocalTransform: Matrix
  Mesh: PrimitiveMesh
  Material: Material3D
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
  member val PlayerAnim = Unchecked.defaultof<AnimatedModel> with get, set

  // Multi-pose demo: second playback state over the same Model + AnimatedMesh,
  // rendered alongside the player at a fixed offset (see View.view).
  member val PlayerAnim2 = Unchecked.defaultof<AnimatedModel> with get, set

  // Bone-attachment demo: weapons parented to the player's handslot bones at
  // draw time (sword in handslot.r, wand in handslot.l), raw-loaded via
  // AssimpNetter — see Program.loadWeaponMesh. LocalTransform is a plain grip
  // offset/scale (raw meshes are in model space, no bone-transform bake
  // needed); KayKit weapons snap onto handslots with identity.
  member val PlayerProps: AttachedProp[] = Array.empty with get, set

  member val ModelCache =
    Dictionary<string, Microsoft.Xna.Framework.Graphics.Model>() with get, set

  member val VisibleLights = ResizeArray<PointLight3D>() with get, set
  member val JumpSound = Unchecked.defaultof<SoundEffect> with get, set
  member val ParticleTexture = Unchecked.defaultof<Texture2D> with get, set
  member val MinimapTexture = Unchecked.defaultof<Texture2D> with get, set

  member val MinimapTexReady = false with get, set

  member val DiagFont = Unchecked.defaultof<SpriteFont> with get, set

  member val GraphicsDevice: GraphicsDevice =
    Unchecked.defaultof<_> with get, set

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
