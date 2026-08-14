namespace Defli3D.MonoGame

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Vfx

// ─────────────────────────────────────────────────────────────
// VfxView — the MonoGame EDGE of the VFX pools. The sim integrates
// backend-neutral Particle3D (positions/sizes/alphas; particles
// spawn at their TRUE world Y — muzzle bursts at the tower top,
// ground effects at 0); the view renders each pool as one
// camera-facing billboard batch (per-kind kenney textures,
// alpha-blended, DepthRead). Scratch buffers are per-kind and
// grow-only — steady state allocates nothing (the deferred commands
// hold their own arrays, so one shared buffer would make every
// command read the LAST kind's particles — Defli's VfxView
// rationale).
//
// Per-kind textures (Defli parity) and albedo tints are view-edge
// concerns per Vfx.fs — no height lift: the sim owns the spawn Y.
// The XNB asset names mirror the raylib loose paths minus the
// extension (see Content.mgcb). MuzzleDust deliberately reuses
// DeathPoof's blackSmoke05 — soft smoke reads as dust, where
// dirt_01 read as sparks.
// ─────────────────────────────────────────────────────────────

module VfxView =
  let inline kindIndex(kind: VfxKind) =
    match kind with
    | Impact -> 0
    | Explosion -> 1
    | DeathPoof -> 2
    | Muzzle -> 3
    | Placement -> 4
    | BaseHit -> 5
    | MuzzleDust -> 6

  /// XNB asset names (the .mgcb mirrors the raylib loose paths minus
  /// the extension — Defli's mapping).
  [<Literal>]
  let ImpactPath = "kenney_particle_pack/spark_01"

  [<Literal>]
  let ExplosionPath = "kenney_smoke_particles/Explosion/explosion03"

  [<Literal>]
  let DeathPoofPath = "kenney_smoke_particles/Black smoke/blackSmoke05"

  [<Literal>]
  let MuzzlePath = "kenney_smoke_particles/Flash/flash00"

  /// The ballista's dust burst (MuzzleDust) reuses the blackSmoke05
  /// smoke texture — soft smoke reads as dust, where dirt_01 read
  /// as sparks at this size.
  [<Literal>]
  let MuzzleDustPath = "kenney_smoke_particles/Black smoke/blackSmoke05"

  [<Literal>]
  let PlacementPath = "kenney_particle_pack/dirt_01"

  [<Literal>]
  let BaseHitPath = "kenney_smoke_particles/Black smoke/blackSmoke05"

  /// Texture asset name per kind.
  let inline textureOf(kind: VfxKind) =
    match kind with
    | Impact -> ImpactPath
    | Explosion -> ExplosionPath
    | DeathPoof -> DeathPoofPath
    | Muzzle -> MuzzlePath
    | MuzzleDust -> MuzzleDustPath
    | Placement -> PlacementPath
    | BaseHit -> BaseHitPath

  /// Per-kind albedo tint. The sim bakes a base color into the
  /// particles (tan for MuzzleDust, white for the rest — Vfx.fs);
  /// this view overrides RGB via tintOf to match, so MuzzleDust's
  /// tint mirrors the sim's tan. Alpha rides the particle's fade.
  let inline tintOf(kind: VfxKind) =
    match kind with
    | Impact -> Color(255, 220, 130)
    | Explosion -> Color(255, 150, 70)
    | DeathPoof -> Color(185, 185, 195)
    | Muzzle -> Color(255, 255, 225)
    | MuzzleDust -> Color(200, 180, 150)
    | Placement -> Color(165, 135, 95)
    | BaseHit -> Color(145, 145, 155)

  let drawPool
    (kind: VfxKind)
    (pool: VfxPool)
    (assets: IAssets)
    struct (positions: _[][], sizes: _[][], colors: _[][], rotations: _[][],
            textures: _[][])
    (buffer: RenderBuffer3D)
    =
    if pool.Count > 0 then
      let idx = kindIndex kind
      let capacity = pool.Particles.Length

      if positions[idx].Length < capacity then
        positions[idx] <- Array.zeroCreate capacity
        sizes[idx] <- Array.zeroCreate capacity
        colors[idx] <- Array.zeroCreate capacity
        rotations[idx] <- Array.zeroCreate capacity

      let tint = tintOf kind

      for i = 0 to pool.Count - 1 do
        let p = pool.Particles[i]

        positions[idx][i] <- Vector3(p.Position.X, p.Position.Y, p.Position.Z)

        sizes[idx][i] <- Vector2(p.Size.X, p.Size.Y)
        colors[idx][i] <- Color(tint.R, tint.G, tint.B, p.Color.A)
        rotations[idx][i] <- p.Rotation

      textures[idx][0] <- assets.Texture(textureOf kind)

      buffer
        .billboardBatch(
          textures[idx],
          positions[idx],
          sizes[idx],
          colors[idx],
          pool.Count,
          rotations = rotations[idx]
        )
        .drop()

[<Sealed>]
type VfxView() =

  /// Per-kind billboardBatch payloads (XNA arrays).
  let positions = Array.init 7 (fun _ -> Array.empty<Vector3>)
  let sizes = Array.init 7 (fun _ -> Array.empty<Vector2>)
  let colors = Array.init 7 (fun _ -> Array.empty<Color>)
  let rotations = Array.init 7 (fun _ -> Array.empty<float32>)
  let textures = Array.init 7 (fun _ -> Array.zeroCreate<Texture2D> 1)

  /// One billboard batch per kind/texture.
  member _.View (ctx: GameContext) (model: VfxModel) (buffer: RenderBuffer3D) =
    let assets = GameContext.getService<IAssets> ctx
    let data = struct (positions, sizes, colors, rotations, textures)
    VfxView.drawPool VfxKind.Impact model.Impact assets data buffer
    VfxView.drawPool VfxKind.Explosion model.Explosion assets data buffer
    VfxView.drawPool VfxKind.DeathPoof model.DeathPoof assets data buffer
    VfxView.drawPool VfxKind.Muzzle model.Muzzle assets data buffer
    VfxView.drawPool VfxKind.MuzzleDust model.MuzzleDust assets data buffer
    VfxView.drawPool VfxKind.Placement model.Placement assets data buffer
    VfxView.drawPool VfxKind.BaseHit model.BaseHit assets data buffer
