module Platformer.Raylib.Types

open System
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Input
open Mibo.Animation
open Platformer
open Platformer.Types
open Platformer.Physics

// -------------------------------------------------------------
// Backend-specific assets
// -------------------------------------------------------------

type SpriteAssets = {
  PlayerSheet: SpriteSheet
  TileTexture: Texture2D
  TorchSheet: SpriteSheet
  ParticleTexture: Texture2D
  CoinNormalMap: Texture2D
  Font: Font
  JumpSound: Sound
}

// -------------------------------------------------------------
// Root Model — composes shared sub-system models + backend-specific state
// -------------------------------------------------------------

type Model() =
  // Shared sub-system models
  member val Physics = PhysicsSystem.init() with get, set
  member val Chunks = WorldGen.Chunks.init 0 with get, set
  member val ParticleState = Particles.init() with get, set
  member val DayNight = DayNightSystem.init() with get, set
  member val Animation = Animation.init() with get, set
  member val Minimap = MinimapSystem.init() with get, set
  member val Diag = Diagnostics.init() with get, set
  // Input
  member val Actions: ActionState<GameAction> = ActionState.empty with get, set
  member val InputMap: InputMap<GameAction> = InputMap.empty with get, set
  // Backend-specific state
  member val Camera: Camera2D = Unchecked.defaultof<_> with get, set
  member val Lighting: LightContext2D = Unchecked.defaultof<_> with get, set
  member val Assets: SpriteAssets = Unchecked.defaultof<_> with get, set
  member val PlayerSprite: AnimatedSprite = Unchecked.defaultof<_> with get, set
  member val TorchSprite: AnimatedSprite = Unchecked.defaultof<_> with get, set
  member val MinimapTexture: Texture2D = Unchecked.defaultof<_> with get, set
  member val MinimapTexReady = false with get, set
  member val ParticleBuffer: Particle2D[] = Array.zeroCreate 512 with get, set

// -------------------------------------------------------------
// Root Msg — wraps sub-system messages + backend-specific messages
// -------------------------------------------------------------

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputMapped of inputs: ActionState<GameAction>
  | ChunkCreated of key: struct (int * int) * chunk: Chunk
  | MinimapReady of colors: Mibo.Color[] * width: int * height: int

// -------------------------------------------------------------
// Inline conversion helpers (shared types → raylib native types)
// -------------------------------------------------------------


let inline toRect(r: Types.Rect) : Rectangle =
  Rectangle(r.X, r.Y, r.Width, r.Height)

let inline toOccluder(o: Types.Occluder) : Occluder2D = { P1 = o.P1; P2 = o.P2 }

let inline toParticle(p: Types.Particle) : Particle2D = {
  Position = p.Position
  Size = p.Size
  Rotation = p.Rotation
  SourceRect = Rectangle(0.0f, 0.0f, 1.0f, 1.0f)
  Color = RaylibColor.toRaylibColor p.Color
}
