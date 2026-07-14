/// Animated tile-effect frame data from the spritesheet-tiles-default atlas.
/// Sprite positions extracted directly from spritesheet-tiles-default.xml.
/// Each frame is 64x64 pixels. Backend projects convert these to their own
/// Rectangle type when building a SpriteSheet.
module Platformer.TileAnimations

/// A single frame's pixel position in the tiles atlas.
[<Struct>]
type TileFrame = { X: float32; Y: float32 }

/// Backend-agnostic animation definition for a tile effect.
[<Struct>]
type TileAnimDef = {
  Name: string
  Frames: TileFrame[]
  FrameDuration: float32
  Loop: bool
}

[<Literal>]
let frameSize = 64.0f

let private f x y = { X = x; Y = y }

/// All animated tile effects. Each entry's frames are listed in playback order.
/// Coordinates from spritesheet-tiles-default.xml:
///   bomb          (195,65) → bomb_active       (260,65)
///   coin_bronze  (1040,65) → coin_bronze_side (1105,65)
///   coin_gold       (0,130) → coin_gold_side    (65,130)
///   coin_silver   (130,130) → coin_silver_side (195,130)
///   flag_blue_a   (780,130) → flag_blue_b      (845,130)
///   flag_green_a  (910,130) → flag_green_b     (975,130)
///   flag_red_a   (1105,130) → flag_red_b         (0,195)
///   flag_yellow_a  (65,195) → flag_yellow_b    (130,195)
///   torch_on_a    (65,1105) → torch_on_b       (130,1105)
let definitions: TileAnimDef[] = [|
  {
    Name = "bomb"
    Frames = [| f 195.0f 65.0f; f 260.0f 65.0f |]
    FrameDuration = 0.3f
    Loop = true
  }
  {
    Name = "coin_bronze"
    Frames = [| f 1040.0f 65.0f; f 1105.0f 65.0f |]
    FrameDuration = 0.15f
    Loop = true
  }
  {
    Name = "coin_gold"
    Frames = [| f 0.0f 130.0f; f 65.0f 130.0f |]
    FrameDuration = 0.15f
    Loop = true
  }
  {
    Name = "coin_silver"
    Frames = [| f 130.0f 130.0f; f 195.0f 130.0f |]
    FrameDuration = 0.15f
    Loop = true
  }
  {
    Name = "flag_blue"
    Frames = [| f 780.0f 130.0f; f 845.0f 130.0f |]
    FrameDuration = 0.3f
    Loop = true
  }
  {
    Name = "flag_green"
    Frames = [| f 910.0f 130.0f; f 975.0f 130.0f |]
    FrameDuration = 0.3f
    Loop = true
  }
  {
    Name = "flag_red"
    Frames = [| f 1105.0f 130.0f; f 0.0f 195.0f |]
    FrameDuration = 0.3f
    Loop = true
  }
  {
    Name = "flag_yellow"
    Frames = [| f 65.0f 195.0f; f 130.0f 195.0f |]
    FrameDuration = 0.3f
    Loop = true
  }
  {
    Name = "torch_on"
    Frames = [| f 65.0f 1105.0f; f 130.0f 1105.0f |]
    FrameDuration = 0.15f
    Loop = true
  }
|]
