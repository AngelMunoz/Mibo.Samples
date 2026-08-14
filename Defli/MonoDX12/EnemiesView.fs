namespace Defli.MonoGame

open System
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Defli.State
open Defli.State.Systems
open Defli.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// EnemiesView — enemy sprites, boss aura rings and health bars from
// the frame's Alive/Defs snapshots (the Alive projection's transient
// view, read as plain dictionary values — no graph access at draw).
// ─────────────────────────────────────────────────────────────

module EnemiesView =

  let view
    (ctx: GameContext)
    (aura: AuraView)
    (time: GameTime)
    (camera: CameraState)
    (viewport: Vector2)
    (alive: IReadOnlyDictionary<int<EnemyId>, EnemyView>)
    (defs: IReadOnlyDictionary<int<EnemyId>, EnemyDef>)
    (path: Vector2[])
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture Paths.Sheet

    for KeyValueV(eid, v) in alive do
      defs
      |> ReadOnlyDict.tryGetValue eid
      |> ValueOption.iter(fun def ->
        let isBoss = def.Archetype = Boss

        // Boss aura (Phase 6): the suppression radius as a soft,
        // pulsing glow — the aura shader owns every pixel of the
        // disc; the radius band shimmers on the frame clock.
        if isBoss then
          aura.Draw
            ctx
            camera
            viewport
            v.Pos
            BossAura.Radius
            (Mibo.Color.create 255uy 60uy 60uy 70uy)
            0.9f
            (float32 time.TotalTime.TotalSeconds)
            buffer

        // Heading: fliers fly the straight spawn → base line; the rest
        // aim at the next waypoint (0° = up; MonoGame rotates CW).
        let angle =
          if def.Archetype = Flier then
            let d = path[path.Length - 1] - path[0]
            MathF.Atan2(d.Y, d.X) * 180f / MathF.PI % 360f
          elif v.PathIndex >= path.Length - 1 then
            0f
          else
            let d = path[v.PathIndex + 1] - v.Pos
            MathF.Atan2(d.Y, d.X) * 180f / MathF.PI % 360f

        // Bosses render 1.6× — the silhouette must read at a glance.
        let sizeBoost = if isBoss then 1.6f else 1f

        def.Sprite
        |> Tiles.tryByName
        |> ValueOption.iter(fun tile ->
          // Scale the baked sprite to a consistent ~44px while keeping aspect.
          let scale =
            44f * sizeBoost / max (float32 tile.Width) (float32 tile.Height)

          let w = float32 tile.Width * scale
          let h = float32 tile.Height * scale

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
              |> SpriteState.withLayer Layers.Entities
            )
            .drop())

        // Turret — centered on the body, aimed at the heading plus the
        // def's built-in orientation correction (0° = up in the sheet).
        def.Turret
        |> ValueOption.bind Tiles.tryByName
        |> ValueOption.iter(fun turretTile ->
          let tscale =
            44f * sizeBoost
            / max (float32 turretTile.Width) (float32 turretTile.Height)

          let tw = float32 turretTile.Width * tscale
          let th = float32 turretTile.Height * tscale

          buffer
            .sprite(
              SpriteState.create(
                tex,
                Rectangle(
                  int(v.Pos.X - tw / 2f),
                  int(v.Pos.Y - th / 2f),
                  int tw,
                  int th
                ),
                MapView.tileRect turretTile
              )
              |> SpriteState.withOrigin(Xna.v2(Vector2(tw / 2f, th / 2f)))
              |> SpriteState.withRotation(angle + def.TurretAngle)
              |> SpriteState.withLayer Layers.Entities
            )
            .drop()))

      // Health bar (only when damaged).
      if v.Hp < v.MaxHp then
        let frac = float32 v.Hp / float32 v.MaxHp

        buffer
          .fillRect(
            v.Pos.X - 16f,
            v.Pos.Y - 28f,
            32f,
            4f,
            Color.Black,
            layer = Layers.Entities
          )
          .drop()

        buffer
          .fillRect(
            v.Pos.X - 16f,
            v.Pos.Y - 28f,
            32f * frac,
            4f,
            Color.Red,
            layer = Layers.Entities
          )
          .drop()
