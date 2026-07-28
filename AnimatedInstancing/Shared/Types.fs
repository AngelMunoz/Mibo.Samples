namespace AnimatedInstancing

// ─────────────────────────────────────────────────────────────
// AnimatedInstancing — performance probe for the skinned+instanced
// draw API (Draw.animatedModelInstanced). One shared animated model
// (KayKit Rig_Medium mannequin) drawn as a grid of N instances, each
// with its own playback state and pose, in ONE instanced draw call.
//
// This file carries the backend-neutral crowd spec: tier sizes, grid
// layout math, and the deterministic per-instance variety helpers.
// Both backends build identical crowds from these so frame-times are
// comparable across backends.
// ─────────────────────────────────────────────────────────────

/// Crowd tiers and deterministic per-instance variety helpers.
module CrowdSpec =
  /// Crowd size tiers — keys 1/2/3/4 select one directly, +/- step through.
  let counts = [| 500; 1000; 5000; 10000 |]

  /// Clips mixed through the crowd by instance index (i % 3). Walking_A and
  /// Running_A ship in Rig_Medium_MovementBasic.glb, Idle_A in
  /// Rig_Medium_General.glb — the merged clip set must contain all three.
  let clipNames = [| "Walking_A"; "Running_A"; "Idle_A" |]

  /// World-space distance between grid neighbours on X and Z.
  let spacing = 2.0f

  /// Animation playback rate (frames per second) for every instance.
  let fps = 60.0f

  /// Side length of the square grid that fits `count` instances.
  let gridSide(count: int) = int(ceil(sqrt(float count)))

  /// Clip name for instance `i` — deterministic, index-stable across tiers.
  let clipNameFor(i: int) = clipNames[i % clipNames.Length]

  /// Starting yaw (degrees) for instance `i`.
  let yawDegreesFor(i: int) = float32(i * 37 % 360)

  /// Initial playback frame for instance `i` — desynchronizes the crowd so
  /// neighbours don't run in lockstep.
  let frameOffsetFor(i: int) = float32(i * 13 % 60)

  /// Grid position of instance `i` on XZ, the grid centered on the origin.
  let gridPosition (side: int) (i: int) : struct (float32 * float32) =
    let col = i % side
    let row = i / side
    let center = float32(side - 1) * 0.5f

    struct ((float32 col - center) * spacing, (float32 row - center) * spacing)

  /// Camera orbit distance for the tier's grid: far enough to frame the whole
  /// crowd, never closer than 60 units.
  let cameraDistance(count: int) =
    max 60.0f (float32(gridSide count) * spacing)

/// Logical actions the InputMap binds keys to (see each backend's Program.fs).
[<Struct>]
type GameAction =
  | Tier1
  | Tier2
  | Tier3
  | Tier4
  | TierUp
  | TierDown
  | TogglePause
