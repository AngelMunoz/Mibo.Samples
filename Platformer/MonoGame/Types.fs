module Platformer.MonoGame.Types

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Audio
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Input
open Mibo.Animation
open Platformer.Types

// -------------------------------------------------------------
// Backend-specific assets
// -------------------------------------------------------------

type SpriteAssets = {
  PlayerSheet: SpriteSheet
  TileTexture: Texture2D
  TorchSheet: SpriteSheet
  ParticleTexture: Texture2D
  CoinNormalMap: Texture2D
  Font: SpriteFont
  JumpSound: SoundEffect
}

// -------------------------------------------------------------
// Root Model — composes shared sub-system models + backend-specific state
// -------------------------------------------------------------

type Model() =
  // Shared sub-system models
  member val Physics = Platformer.Physics.PhysicsSystem.init() with get, set
  member val Chunks = Platformer.WorldGen.Chunks.init 0 with get, set
  member val ParticleState = Platformer.Particles.init() with get, set
  member val DayNight = Platformer.DayNightSystem.init() with get, set
  member val Animation = Platformer.Animation.init() with get, set
  member val Minimap = Platformer.MinimapSystem.init() with get, set
  member val Diag = Platformer.Diagnostics.init() with get, set
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

// -------------------------------------------------------------
// Inline conversion helpers (shared types → XNA native types)
// -------------------------------------------------------------

let inline toRect(r: Platformer.Types.Rect) : Rectangle =
  Rectangle(int r.X, int r.Y, int r.Width, int r.Height)

let inline toOccluder(o: Platformer.Types.Occluder) : Occluder2D = {
  P1 = Vector2.op_Implicit o.P1
  P2 = Vector2.op_Implicit o.P2
}

let inline toParticle(p: Platformer.Types.Particle) : Particle2D = {
  Position = Vector2.op_Implicit p.Position
  Size = Vector2.op_Implicit p.Size
  Rotation = p.Rotation
  SourceRect = Rectangle(0, 0, 1, 1)
  Color = MonoGameColor.toMonoGameColor p.Color
}
