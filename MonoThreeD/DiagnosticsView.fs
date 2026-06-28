module MonoThreeD.Diagnostics

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open MonoThreeD.Types
open MonoThreeD.Constants

// Pixel position for the next diagnostics line.
let private startY = 30.0f
let private lineH = 25.0f

let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  let diag = model.Diagnostics
  let mutable y = startY

  let inline writeLine (text: string) (color: Color) =
    buffer
    |> Draw.text(
      TextState.create(diag.Font, text, Vector2(10.0f, y))
      |> TextState.withScale 0.75f
      |> TextState.withColor color
      |> TextState.withLayer 0<RenderLayer>
    )
    |> Draw.drop

    y <- y + lineH

  writeLine
    $"FPS: {diag.Fps}  Chunks: {diag.ChunkCount}  Score: {diag.Score}"
    Color.Yellow

  writeLine
    $"Time: {diag.TimeOfDay:F1}h  Pos: ({diag.PlayerX:F0},{diag.PlayerY:F0},{diag.PlayerZ:F0})  Grounded: {diag.IsGrounded}  Particles: {diag.ParticleCount}"
    Color.Yellow
