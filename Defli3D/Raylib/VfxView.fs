namespace Defli3D.Raylib

open System
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Systems
open Defli3D.State.Systems.Vfx

// ─────────────────────────────────────────────────────────────
// VfxView — the raylib EDGE of the VFX pools: the sim stores
// backend-neutral Particle3D (position/size/alpha); the view maps
// them into ONE billboardBatch per pool through persistent scratch
// buffers OWNED BY THE VIEW (instance state — no per-frame
// allocation, mirroring Defli's VfxView).
//
// Each effect kind gets its real kenney texture (Defli parity — the
// 2D version used one texture per kind; see Raylib.fsproj for the
// copied asset packs). MuzzleDust shares the smoke sprite — the sim
// pre-tints those particles tan, so the view draws p.Color as-is.
// Particles spawn at their TRUE world Y — the
// sim carries muzzle heights (TowerLayout.muzzleY / VfxMsg's y) — so
// the view draws at Position.Y as-is (the old per-kind lift hack is
// dead). Textures resolve lazily through IAssets (cached by path)
// the first frame a kind's pool is non-empty.
// ─────────────────────────────────────────────────────────────

module VfxView =
  let inline kindIndex(kind: VfxKind) =
    match kind with
    | Impact -> 0
    | Explosion -> 1
    | DeathPoof -> 2
    | Muzzle -> 3
    | MuzzleDust -> 6
    | Placement -> 4
    | BaseHit -> 5

  /// Per-kind texture path (kenney packs, loose assets — Defli's
  /// mapping).
  [<Literal>]
  let ImpactPath = "kenney_particle_pack/spark_01.png"

  [<Literal>]
  let ExplosionPath = "kenney_smoke_particles/Explosion/explosion03.png"

  [<Literal>]
  let DeathPoofPath = "kenney_smoke_particles/Black smoke/blackSmoke05.png"

  [<Literal>]
  let MuzzlePath = "kenney_smoke_particles/Flash/flash00.png"

  /// Dust — the smoke sprite plus the sim's slow-expanding params
  /// and tan base color read as dust (dirt_01 read as sparks). The
  /// sim spawns these pre-tinted tan (Color.rgb 200 180 150) and the
  /// view draws p.Color as-is — no view-side tint.
  [<Literal>]
  let MuzzleDustPath = "kenney_smoke_particles/Black smoke/blackSmoke05.png"

  [<Literal>]
  let PlacementPath = "kenney_particle_pack/dirt_01.png"

  [<Literal>]
  let BaseHitPath = "kenney_smoke_particles/Black smoke/blackSmoke05.png"

  /// Texture path per kind.
  let inline textureOf(kind: VfxKind) =
    match kind with
    | Impact -> ImpactPath
    | Explosion -> ExplosionPath
    | DeathPoof -> DeathPoofPath
    | Muzzle -> MuzzlePath
    | MuzzleDust -> MuzzleDustPath
    | Placement -> PlacementPath
    | BaseHit -> BaseHitPath

  let inline ensure (arr: 'T[]) (capacity: int) : 'T[] =
    if arr.Length >= capacity then
      arr
    else
      Array.zeroCreate capacity

  /// One billboardBatch per pool: positions/sizes/colors recorded
  /// into the pool's scratch, alpha from the sim's fade.
  let drawPool
    (kind: VfxKind)
    (pool: VfxPool)
    (assets: IAssets)
    struct (positions: _[][], sizes: _[][], colors: _[][], textures: _[][])
    (buffer: RenderBuffer3D)
    =
    if pool.Count > 0 then
      let idx = kindIndex kind
      let capacity = pool.Particles.Length

      let texScratch = ensure textures[idx] capacity
      let posScratch = ensure positions[idx] capacity
      let sizeScratch = ensure sizes[idx] capacity
      let colorScratch = ensure colors[idx] capacity

      textures[idx] <- texScratch
      positions[idx] <- posScratch
      sizes[idx] <- sizeScratch
      colors[idx] <- colorScratch

      let tex = assets.Texture(textureOf kind)

      for i = 0 to pool.Count - 1 do
        let p = pool.Particles[i]

        texScratch[i] <- tex

        // The sim carries the true world Y (muzzle bursts spawn at
        // the tower top) — draw at Position.Y as-is.
        posScratch[i] <- p.Position

        sizeScratch[i] <- p.Size
        colorScratch[i] <- RaylibColor.toRaylibColor p.Color

      buffer
        .billboardBatch(
          texScratch,
          posScratch,
          sizeScratch,
          colorScratch,
          pool.Count
        )
        .drop()

[<Sealed>]
type VfxView() =

  /// Grow-only scratch per pool — all seven kinds share the layout.
  let textures = Array.init 7 (fun _ -> Array.empty<Texture2D>)
  let positions = Array.init 7 (fun _ -> Array.empty<Vector3>)
  let sizes = Array.init 7 (fun _ -> Array.empty<Vector2>)
  let colors = Array.init 7 (fun _ -> Array.empty<Raylib_cs.Color>)

  /// The view: one billboardBatch per kind/pool.
  member _.View (ctx: GameContext) (model: VfxModel) (buffer: RenderBuffer3D) =
    let assets = GameContext.getService<IAssets> ctx
    let data = struct (positions, sizes, colors, textures)
    VfxView.drawPool VfxKind.Impact model.Impact assets data buffer
    VfxView.drawPool VfxKind.Explosion model.Explosion assets data buffer
    VfxView.drawPool VfxKind.DeathPoof model.DeathPoof assets data buffer
    VfxView.drawPool VfxKind.Muzzle model.Muzzle assets data buffer
    VfxView.drawPool VfxKind.MuzzleDust model.MuzzleDust assets data buffer
    VfxView.drawPool VfxKind.Placement model.Placement assets data buffer
    VfxView.drawPool VfxKind.BaseHit model.BaseHit assets data buffer
