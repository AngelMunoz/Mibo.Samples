namespace Defli3D.State

// ─────────────────────────────────────────────────────────────
// Tower layout — the single source of truth for tower composition
// heights: the body stack (kit pieces by level), the weapon's
// resting Y, the body top (the HUD Lv-tag anchor) and the
// approximate muzzle height. Shared by both render backends (body
// stack + weapon placement) and the sim's muzzle-origin math
// (Towers.Fired carries Height; muzzle VFX bursts spawn at it).
//
// Convention: 1 cell = 1 world unit; tower tiles' top surface is
// at y = 0.2; the kit models are bottom-anchored (min-Y = 0, verified
// via BoneProbe), so a piece's SizeY is also its rise. The body is
// stacked WITHOUT the roof — the weapon mounts flush on the top piece.
//
// Per-level composition (base + bottom always; the gun sits on the
// topmost piece):
//   level 1 — base + bottom                       (minimal stub)
//   level 2 — base + bottom + top-A
//   level 3 — base + bottom + middle + top-B
//   level 4 — base + bottom + middle×2 + top-B
//   level 5 — base + bottom + middle×3 + top-C
// Each level adds one piece, so the height grows monotonically and
// weaponY (= baseY + scaled stackHeight) rises with the level.
//
// Variety (independent of level): the bottom and middle VARIANT
// (A/B/C) is chosen per tower from a deterministic cell-hash seed.
// Bottom/middle A/B/C share SizeY within a family (verified in
// Models.fs), so variety does not change the stack height — the gun
// stays exactly at the top, no overlaps. The top variant is
// level-driven (A→B→C). The base is towerRoundBase for BOTH families
// (no square-base asset ships in the kit).
// ─────────────────────────────────────────────────────────────

module TowerLayout =

  /// The ONE shared visual scale for tower bodies + weapons.
  /// 1 = model size on a 1-unit tile (the kit pieces are baked near
  /// tile size, so 0.6 shrinks the stack to read as a tower on its
  /// tile). Was duplicated per backend (0.8); the sim's muzzle math
  /// needs it shared.
  let towerScale = 0.6f

  // ── Per-family variant pools ───────────────────────────────
  // Within a tier the A/B/C variants share SizeY (round bottom/middle
  // 0.6, square 0.5), so picking a variant changes only the silhouette,
  // never the stack height.
  let private roundBottoms = [|
    Models.towerRoundBottomA
    Models.towerRoundBottomB
    Models.towerRoundBottomC
  |]

  let private roundMiddles = [|
    Models.towerRoundMiddleA
    Models.towerRoundMiddleB
    Models.towerRoundMiddleC
  |]

  let private roundTops = [|
    Models.towerRoundTopA
    Models.towerRoundTopB
    Models.towerRoundTopC
  |]

  let private squareBottoms = [|
    Models.towerSquareBottomA
    Models.towerSquareBottomB
    Models.towerSquareBottomC
  |]

  let private squareMiddles = [|
    Models.towerSquareMiddleA
    Models.towerSquareMiddleB
    Models.towerSquareMiddleC
  |]

  let private squareTops = [|
    Models.towerSquareTopA
    Models.towerSquareTopB
    Models.towerSquareTopC
  |]

  /// The shared foundation pad for BOTH families (no tower-square-base
  /// ships in the kit, so square/cannon reuses the round pad).
  let private basePiece = Models.towerRoundBase

  /// Deterministic variant seed (0..2) from a tower's cell — stable
  /// across level-ups (the cell never changes) so a tower keeps its
  /// bottom/middle detailing as it grows.
  let variantSeed (cx: int) (cy: int) : int = ((cx * 7 + cy * 13) % 3 + 3) % 3

  /// Middle-floor count per level (the tower gains one floor per level
  /// past L2). L1/L2 have no middle.
  let inline private middleCount(level: int) : int =
    match level with
    | 1
    | 2 -> 0
    | 3 -> 1
    | 4 -> 2
    | _ -> 3 // 5 and above clamp to 3

  /// The cap (top) variant index per level, or -1 when there is no cap
  /// (L1 only). A at L2, B at L3-4, C at L5+.
  let inline private capIndex(level: int) : int =
    match level with
    | 1 -> -1
    | 2 -> 0
    | 3
    | 4 -> 1
    | _ -> 2

  /// Builds ONE (family, level, variant) stack array (bottom→top, no
  /// roof): [base; bottom(variant); middle(variant)×middleCount; cap?].
  /// Runs once at module init to precompute the per-family level tables
  /// — stackFor hands out those cached arrays, so the per-frame render
  /// path (both backends call stackFor per tower per frame) allocates
  /// nothing.
  let inline private buildStack
    (level: int)
    (variant: int)
    (bottoms: _[], middles: _[], tops: _[])
    : ModelInfo[] =
    let mc = middleCount level
    let cap = capIndex level
    let count = 2 + mc + (if cap >= 0 then 1 else 0)
    let stack = Array.zeroCreate<ModelInfo> count
    stack[0] <- basePiece
    stack[1] <- bottoms[variant]

    for i = 0 to mc - 1 do
      stack[2 + i] <- middles[variant]

    if cap >= 0 then
      stack[2 + mc] <- tops[cap]

    stack

  /// Cached body stacks per (family, level, variant), indexed by
  /// (level-1)*3 + variant (levels 1..5, variants 0..2). Public so the
  /// inline accessors below can reference them; callers treat the
  /// returned array as read-only.
  let roundStacks = [|
    for level in 1..5 do
      for variant in 0..2 do
        buildStack level variant (roundBottoms, roundMiddles, roundTops)
  |]

  let squareStacks = [|
    for level in 1..5 do
      for variant in 0..2 do
        buildStack level variant (squareBottoms, squareMiddles, squareTops)
  |]

  /// The tower BODY as a stack of pre-cut kit pieces, bottom→top,
  /// WITHOUT a roof — the weapon rests on the top piece. `variantSeed`
  /// (0..2, from the tower cell) selects the bottom/middle detailing;
  /// it does not change the stack height. Returns a SHARED cached array
  /// — callers must treat it as read-only.
  let inline stackFor
    (def: TowerDef)
    (level: int)
    (variantSeed: int)
    : ModelInfo[] =
    let stacks = if def.Key = "cannon" then squareStacks else roundStacks
    let lv = min (max level 1) 5
    let v = variantSeed % 3
    stacks[(lv - 1) * 3 + v]

  /// Sum of the stack pieces's SizeY (unscaled model heights). Variant-
  /// independent (bottom/middle variants share SizeY), so the canonical
  /// variant 0 is used. Reads the cached stack — no allocation.
  let inline stackHeight (def: TowerDef) (level: int) : float32 =
    stackFor def level 0 |> Array.sumBy(fun piece -> piece.SizeY)

  /// The tile top — tower bodies sit on it (the ground tiles are
  /// bottom-anchored with their top surface at y = 0.2).
  let baseY = 0.2f

  /// The weapon's resting Y — flush ON the top piece (all pieces are
  /// bottom-anchored, so the stack top is baseY + scaled stack height).
  /// The tower body top is the same height by construction.
  let inline weaponY (def: TowerDef) (level: int) : float32 =
    baseY + stackHeight def level * towerScale

  /// The tower body's top — the HUD Lv-tag anchor. Identical to
  /// weaponY (the weapon rests on the top piece).
  let inline towerTop (def: TowerDef) (level: int) : float32 = weaponY def level

  /// APPROXIMATE barrel-tip height: the weapon rests at weaponY and
  /// the muzzle sits ~75 % of the weapon's scaled height up (an
  /// approximation — the sim integrates XZ only; this Y feeds the
  /// projectile spawn and muzzle VFX for presentation).
  let inline muzzleY (def: TowerDef) (level: int) : float32 =
    weaponY def level + def.WeaponModel.SizeY * 0.75f * towerScale
