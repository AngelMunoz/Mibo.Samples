module Defli3D.State.Systems.Vfx

open System
open System.Collections.Generic
open System.Numerics
open Mibo
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// VFX sub-system — the ONE deliberately non-adaptive system in the
// world (plan §4.10): per-particle adaptive cells would be node
// churn for fire-and-forget presentation. Pooled particles pattern
// (Mibo pooled-particles), one pool per effect kind.
//
// Kinds (burst semantics unchanged from Defli):
//   Impact    — tight, fast-fading sparks at the hit
//   Explosion — a clustered fireball: quick fade
//   DeathPoof — slow puffs that EXPAND, rise, linger
//   Muzzle    — a single stationary flash at the barrel
//   MuzzleDust— a few slow tan dust puffs at the barrel (the
//              ballista is a bow — no fire flash)
//   Placement — low dust clumps that settle fast
//   BaseHit   — a bigger smoke cloud at the base
//
// The motion model is per-kind (paramsOf): velocity DAMPING stalls
// the spray near the origin (no more fly-away balls), RISE lifts
// particles off the ground plane (+Y — the 2D version drifted
// up-screen), and GROWTH expands puffs as they fade. Deterministic
// bursts — no RNG stream (index-based angles/speed tiers;
// golden-angle rotation spread).
//
// 3D port: a burst is a world-space point — the msg's Vector2 pos
// is x/z and its y IS the world-space spawn height (previously
// particles always spawned at y = 0 and the VIEWS hardcoded a
// muzzle lift; that hack is gone — the sim carries muzzle heights
// now). Velocities stay XZ; the per-kind rise is a Y drift on top
// of the spawn height so impacts read from the orbit camera.
// Sizes/speeds are world units (Defli's px ÷ 64). The sim
// integrates only backend-neutral data — billboard vs
// instanced-mesh rendering and mesh/material handle caches are
// VIEW-edge concerns.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type VfxKind =
  | Impact
  | Explosion
  | DeathPoof
  | Muzzle
  | MuzzleDust
  | Placement
  | BaseHit

/// Backend-neutral particle: the sim only integrates positions,
/// sizes and alphas; the frontend maps it to its draw primitive at
/// the view edge (billboard quad / small instanced mesh).
/// Position is world-space (x, y-up, z); Size is the quad/extent
/// size in world units; Rotation is free for the view to interpret.
[<Struct>]
type Particle3D = {
  Position: Vector3
  Size: Vector2
  Rotation: float32
  Color: Color
}

/// One pooled particle store (SoA-ish: particles + parallel XZ
/// velocities — the Y drift is the per-kind rise, applied undamped).
[<Sealed>]
type VfxPool(capacity: int) =
  member val Particles = Array.zeroCreate<Particle3D> capacity with get, set
  member val Velocities = Array.zeroCreate<Vector2> capacity with get, set
  member val Count = 0 with get, set

type VfxModel() =
  member val Impact = VfxPool 256 with get, set
  member val Explosion = VfxPool 256 with get, set
  member val DeathPoof = VfxPool 256 with get, set
  member val Muzzle = VfxPool 128 with get, set
  member val MuzzleDust = VfxPool 128 with get, set
  member val Placement = VfxPool 128 with get, set
  member val BaseHit = VfxPool 128 with get, set

module Vfx =

  let init() = VfxModel()

  /// Per-kind parameters (Defli's px values ÷ 64, world units):
  ///   count  — particles per burst
  ///   speed  — base XZ spray speed (units/s; tier-multiplied at spawn)
  ///   size   — initial quad size (world units; tiles are 1×1)
  ///   fade   — alpha per second (255/fade = seconds of life)
  ///   growth — size delta per second (smoke expands, sparks shrink)
  ///   rise   — upward (+Y) drift in units/s, applied undamped so
  ///            effects lift off the ground plane and read in 3D
  ///            (Impact/Muzzle had no 2D drift; Impact gets a small
  ///            pop so sparks don't hide in the flat XZ plane)
  ///   damp   — velocity decay per second (stalls the spray near the
  ///            origin — the anti "fly-away balls" term)
  let inline private paramsOf(kind: VfxKind) =
    match kind with
    | Impact -> struct (6, 1.4f, 0.14f, 700f, -0.09f, 0.2f, 6f)
    | Explosion -> struct (7, 0.7f, 0.69f, 420f, 0.4f, 0.16f, 5f)
    | DeathPoof -> struct (5, 0.4f, 0.56f, 110f, 0.22f, 0.28f, 3f)
    | Muzzle -> struct (1, 0f, 0.5f, 640f, 0.31f, 0f, 0f)
    | MuzzleDust -> struct (4, 0.25f, 0.45f, 300f, 0.25f, 0.35f, 4f)
    | Placement -> struct (5, 0.4f, 0.28f, 260f, 0.16f, 0.09f, 4f)
    | BaseHit -> struct (6, 0.47f, 0.63f, 130f, 0.25f, 0.19f, 3f)

  /// Smallest clamped particle size in world units (the 2D floor of
  /// 1 px has no meaning at 1 unit/tile scale).
  let inline private minSize() = Vector2(0.02f, 0.02f)

  /// Cold path: spawn a burst into the kind's pool (deterministic
  /// spread — index-based angles, three speed tiers, golden-angle
  /// rotation so overlapping puffs don't read as copies). The burst
  /// position is a world-space point (pos is x/z; y is the spawn
  /// height: muzzle bursts spawn at the tower top, ground effects
  /// pass 0).
  let burst
    (kind: VfxKind)
    (pos: Vector2)
    (y: float32)
    (model: VfxModel)
    : unit =
    let struct (count, speed, size, _, _, _, _) = paramsOf kind

    // Dust puffs spawn tan (ground dust) — the raylib view draws
    // the particle color directly; MonoGame tints per kind.
    // Everything else stays white.
    let baseColor =
      match kind with
      | VfxKind.MuzzleDust -> Color.rgb 200uy 180uy 150uy
      | _ -> Color.White

    let pool =
      match kind with
      | Impact -> model.Impact
      | Explosion -> model.Explosion
      | DeathPoof -> model.DeathPoof
      | Muzzle -> model.Muzzle
      | MuzzleDust -> model.MuzzleDust
      | Placement -> model.Placement
      | BaseHit -> model.BaseHit

    let mutable i = 0

    while i < count && pool.Count < pool.Particles.Length do
      let angle = float32 i / float32 count * 2f * MathF.PI
      let tier = float32(i % 3 + 1)
      let dir = Vector2(MathF.Cos angle, MathF.Sin angle)
      let velocity = dir * (speed * tier)

      pool.Particles[pool.Count] <-
        {
          Position = Vector3(pos.X, y, pos.Y)
          Size = Vector2(size, size)
          Rotation = float32((i * 137) % 360)
          Color = baseColor
        }

      pool.Velocities[pool.Count] <- velocity
      pool.Count <- pool.Count + 1
      i <- i + 1

  let inline private stepPool dt (kind: VfxKind) (pool: VfxPool) =
    let struct (_, _, _, fadeSpeed, growth, rise, damp) = paramsOf kind
    let dampMul = max 0f (1f - damp * dt)
    let growVec = Vector2(growth * dt, growth * dt)
    let riseY = rise * dt
    let minSize = minSize()

    for i in 0 .. pool.Count - 1 do
      let p = pool.Particles[i]
      let vel = pool.Velocities[i] * dampMul
      pool.Velocities[i] <- vel

      pool.Particles[i] <-
        {
          p with
              Position =
                Vector3(
                  p.Position.X + vel.X * dt,
                  // Rise is the undamped +Y drift (the 2D up-screen term).
                  p.Position.Y + riseY,
                  p.Position.Z + vel.Y * dt
                )
              Size = Vector2.Max(p.Size + growVec, minSize)
        }

    let fadeAmount = fadeSpeed * dt
    let mutable write = 0

    for read in 0 .. pool.Count - 1 do
      let p = pool.Particles[read]
      // Clamp in FLOAT before the byte conversion: `byte` of a
      // negative float wraps (conv.u1 takes the low byte — -5.67
      // becomes 251), resurrecting nearly-dead particles at full
      // alpha — the "muzzle keeps flashing" ghost.
      let newAlpha = byte(max 0f (float32 p.Color.A - fadeAmount))

      if newAlpha > 0uy then
        pool.Particles[write] <-
          {
            p with
                Color = { p.Color with A = newAlpha }
          }

        pool.Velocities[write] <- pool.Velocities[read]
        write <- write + 1

    pool.Count <- write

  /// Hot path: damp/integrate velocities, rise + grow, fade, compact
  /// (in place, zero alloc). Velocities are compacted in parallel.
  let tick (dt: float32) (model: VfxModel) : unit =
    stepPool dt VfxKind.Impact model.Impact
    stepPool dt VfxKind.Explosion model.Explosion
    stepPool dt VfxKind.DeathPoof model.DeathPoof
    stepPool dt VfxKind.Muzzle model.Muzzle
    stepPool dt VfxKind.MuzzleDust model.MuzzleDust
    stepPool dt VfxKind.Placement model.Placement
    stepPool dt VfxKind.BaseHit model.BaseHit
