namespace Defli.MonoGame

open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Defli.World
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// TowersView — base plates, heads and level tags from the frame's
// TowerStatics/TowerLevels snapshots.
// ─────────────────────────────────────────────────────────────

module TowersView =

  let view
    (ctx: GameContext)
    (statics: IReadOnlyDictionary<int<TowerId>, TowerStatic>)
    (levels: IReadOnlyDictionary<int<TowerId>, int>)
    (cellSize: Vector2)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture Paths.Sheet
    let font = assets.Font Paths.Font
    let size = cellSize

    for KeyValueV(tid, s) in statics do
      let center = Cells.center s.Cell cellSize

      let cellRect =
        Rectangle(
          int(center.X - size.X / 2f),
          int(center.Y - size.Y / 2f),
          int size.X,
          int size.Y
        )

      // Base plate.
      buffer
        .sprite(
          SpriteState.create(tex, cellRect, MapView.tileRect Tiles.turretBaseA)
          |> SpriteState.withLayer Layers.Entities
        )
        .drop()

      // Head (the def's sprite — rocket pod).
      s.Def.Sprite
      |> Tiles.tryByName
      |> ValueOption.iter(fun tile ->
        buffer
          .sprite(
            SpriteState.create(tex, cellRect, MapView.tileRect tile)
            |> SpriteState.withLayer Layers.Entities
          )
          .drop())

      // Upgrade level tag (Lv 2+), world-space above the tower.
      levels
      |> ReadOnlyDict.tryGetValue tid
      |> ValueOption.iter(fun level ->
        if level > 1 then
          buffer
            .text(
              font,
              $"Lv %d{level}",
              center - Vector2(16f, size.Y / 2f + 18f),
              0.55f,
              layer = Layers.Entities
            )
            .drop())
