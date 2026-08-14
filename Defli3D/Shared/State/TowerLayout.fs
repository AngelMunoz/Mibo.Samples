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
// at y = 0.2; the kit models are bottom-anchored (min-Y = 0), so
// a piece's SizeY is also its rise. All pieces are the pre-cut
// stack parts WITHOUT the roof — the weapon mounts flush on the
// top piece.
// ─────────────────────────────────────────────────────────────

module TowerLayout =

  /// The ONE shared visual scale for tower bodies + weapons.
  /// 1 = model size on a 1-unit tile (the kit pieces are baked near
  /// tile size, so 0.6 shrinks the stack to read as a tower on its
  /// tile). Was duplicated per backend (0.8); the sim's muzzle math
  /// needs it shared.
  let towerScale = 0.6f

  /// The kit piece family for a tower def: square for the cannon,
  /// round for everything else (arrow + frost today). Returns the
  /// family's five pieces — the bottom/middle a-variants plus the
  /// three top variants stackFor maps levels onto.
  let roundPieces =
    struct (Models.towerRoundBottomA,
            Models.towerRoundMiddleA,
            Models.towerRoundTopA,
            Models.towerRoundTopB,
            Models.towerRoundTopC)

  let squarePieces =
    struct (Models.towerSquareBottomA,
            Models.towerSquareMiddleA,
            Models.towerSquareTopA,
            Models.towerSquareTopB,
            Models.towerSquareTopC)

  let inline family(def: TowerDef) =
    if def.Key = "cannon" then squarePieces else roundPieces

  /// Builds ONE level's stack array (bottom→top, no roof). Runs once
  /// at module init to precompute the per-family level tables —
  /// stackFor hands out those cached arrays, so the per-frame render
  /// path (both backends call stackFor per tower per frame) allocates
  /// nothing.
  let inline buildStack
    (level: int)
    (struct (bottom, middle, topA, topB, topC))
    : ModelInfo[] =
    let middleCount =
      match level with
      | 1 -> 0
      | 2
      | 3 -> 1
      | 4
      | 5 -> 2
      | _ -> 3

    let top =
      if level >= 5 then topC
      elif level >= 3 then topB
      else topA

    let stack = Array.zeroCreate<ModelInfo>(2 + middleCount)
    stack[0] <- bottom

    for i in 1..middleCount do
      stack[i] <- middle

    stack[middleCount + 1] <- top
    stack

  /// Cached body stacks per family, indexed by level − 1 (levels
  /// 1..6; anything above 6 clamps to the level-6 stack).
  let roundStacks = [| for level in 1..6 -> buildStack level roundPieces |]

  let squareStacks = [| for level in 1..6 -> buildStack level squarePieces |]

  /// The tower BODY as a stack of pre-cut kit pieces, bottom→top,
  /// WITHOUT a roof — the weapon rests on the top piece. Level →
  /// stack (bottom/middle stay on the a variants; only the top
  /// varies a/b/c):
  ///   level 1  — bottom-a + top-a
  ///   level 2  — bottom-a + middle-a + top-a
  ///   level 3  — bottom-a + middle-a + top-b
  ///   level 4  — bottom-a + middle-a ×2 + top-b
  ///   level 5  — bottom-a + middle-a ×2 + top-c
  ///   level 6+ — bottom-a + middle-a ×3 + top-c
  /// Returns a SHARED cached array — callers must treat it as
  /// read-only.
  let inline stackFor (def: TowerDef) (level: int) : ModelInfo[] =
    let stacks = if def.Key = "cannon" then squareStacks else roundStacks
    stacks[min (max level 1) 6 - 1]

  /// Sum of the stack pieces' SizeY (unscaled model heights). Reads
  /// the cached stack — no allocation.
  let inline stackHeight (def: TowerDef) (level: int) : float32 =
    stackFor def level |> Array.sumBy(fun piece -> piece.SizeY)

  /// The tile top — tower bodies sit on it (the ground tiles are
  /// bottom-anchored with their top surface at y = 0.2).
  let baseY = 0.2f

  /// The weapon's resting Y — flush ON the top piece (all pieces
  /// are bottom-anchored, so the stack top is baseY + scaled stack
  /// height). The tower body top is the same height by construction.
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
