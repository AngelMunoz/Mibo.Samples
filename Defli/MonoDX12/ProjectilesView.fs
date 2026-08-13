namespace Defli.MonoGame

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Defli.State
open Defli.State.Systems

// ─────────────────────────────────────────────────────────────
// ProjectilesView — the shell sprite per row from the frame's
// Homing projection snapshot.
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

  let view
    (ctx: GameContext)
    (homing: IReadOnlyDictionary<int<ProjectileId>, HomingView>)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture Paths.Sheet

    for KeyValueV(pid, v) in homing do
      let tile =
        v.Sprite
        |> Tiles.tryByName
        |> ValueOption.defaultValue Tiles.rocketSmall

      let scale = 28f / max (float32 tile.Width) (float32 tile.Height)
      let w = float32 tile.Width * scale
      let h = float32 tile.Height * scale

      // Heading toward the (possibly last recorded) target position
      // (0° = up; MonoGame rotates CW).
      let d = v.TargetPos - v.Pos
      let angle = 90f + MathF.Atan2(d.Y, d.X) * 180f / MathF.PI

      buffer
        .sprite(
          SpriteState.create(
            tex,
            Rectangle(
              int(v.Pos.X - w / 2f),
              int(v.Pos.Y - h / 2f),
              int w,
              int h
            ),
            MapView.tileRect tile
          )
          |> SpriteState.withOrigin(Xna.v2(Vector2(w / 2f, h / 2f)))
          |> SpriteState.withRotation angle
          |> SpriteState.withLayer Layers.Projectiles
        )
        .drop()
