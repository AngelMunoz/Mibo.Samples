namespace Defli3D.State

// ─────────────────────────────────────────────────────────────
// Tower layout — the single source of truth for tower composition
// per CHASSIS: the body pieces, the gun mount height, the HUD tag
// anchor (towerTop) and the muzzle height. Shared by both render
// backends (body + mounts) and the sim's firing solution
// (Towers.Fired carries muzzleY and the muzzle offset basis).
//
// A tower's LEVEL is its POWER — NEVER its height: every chassis is
// COMPLETE from placement (the gun/deck exists from the get-go; a
// fresh tower never looks like a stub). The per-chassis bodies:
//
//   Emplacement — [base pad]; the gun sits on the pad — the kit's
//     gun models carry their own mounts.
//   Deck letter — [bottom-a/b/c; middle-a/b/c; top-a/b/c]: the
//     letter (part of the def — a arrows, b cannons, c bullets)
//     styles EVERY piece. The MIDDLE is the rotating gun deck.
//   Bunker — [bottom; middle(bay); top]: the square family, the
//     only big-gun chassis allowed a middle (the gun bay).
//   Keep letter — [build-a/b/c]: one prebuilt self-armed tower.
//   Battery — [base; bottom; top]: the heavy-gun platform (catapult,
//     large ballista) — NO middle (middles are gun decks), but it
//     keeps its foundation pad.
//
// Convention: 1 cell = 1 world unit; tower tiles' top surface is
// at y = 0.2; the kit models are bottom-anchored (min-Y = 0,
// verified via BoneProbe), so a piece's SizeY is also its rise.
//
// Variety (battery/bunker only): the bottom/top VARIANT (A/B/C) is
// chosen per tower from a deterministic cell-hash seed. Deck towers
// have NO random variant — their letter is their weapon.
// ─────────────────────────────────────────────────────────────

module TowerLayout =

  /// The ONE shared visual scale for tower bodies + weapons.
  /// 1 = model size on a 1-unit tile. Shared with the sim's muzzle
  /// math.
  let towerScale = 0.6f

  // ── Per-family variant pools ───────────────────────────────
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

  let private roundBuilds = [|
    Models.towerRoundBuildA
    Models.towerRoundBuildB
    Models.towerRoundBuildC
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

  /// The round family's foundation pad (emplacement body + battery
  /// foot).
  let private roundBase = Models.towerRoundBase

  /// Deterministic variant seed (0..2) from a tower's cell — used by
  /// the battery/bunker detailing (their letters carry no weapon
  /// meaning). Stable across level-ups (the cell never changes).
  let variantSeed (cx: int) (cy: int) : int = ((cx * 7 + cy * 13) % 3 + 3) % 3

  /// Deck body: [bottom; middle(gun deck); top] — every piece styled
  /// by the def's letter (an "a" tower is all a-parts).
  let private deckStack(letter: int) : ModelInfo[] = [|
    roundBottoms[letter]
    roundMiddles[letter]
    roundTops[letter]
  |]

  /// Battery body: [base; bottom; top] — the heavy-gun platform: it
  /// keeps its foundation pad and has NO middle (middles are gun
  /// decks; big guns sit on the open top).
  let private batteryStack(variant: int) : ModelInfo[] = [|
    roundBase
    roundBottoms[variant]
    roundTops[variant]
  |]

  /// Bunker body: [bottom; middle(bay); top] — the square family,
  /// the only big-gun chassis with a middle (the gun sits inside).
  let private bunkerStack(variant: int) : ModelInfo[] = [|
    squareBottoms[variant]
    squareMiddles[variant]
    squareTops[variant]
  |]

  /// The emplacement body: the round base pad alone — the kit's gun
  /// models carry their own mounts (the ballista IS a framed
  /// weapon), so the gun seats directly on the pad.
  let private emplacementStack = [| roundBase |]

  /// The keep body: ONE prebuilt piece, fixed by the def's letter.
  let private keepStack(letter: int) : ModelInfo[] = [|
    roundBuilds[(letter % 3 + 3) % 3]
  |]

  // Precomputed bodies (module init — the per-frame path allocates
  // nothing; callers treat the arrays as read-only).
  let private deckStacks = [| for letter in 0..2 -> deckStack letter |]

  let private batteryStacks = [| for v in 0..2 -> batteryStack v |]

  let private bunkerStacks = [| for v in 0..2 -> bunkerStack v |]

  /// The tower BODY as a stack of kit pieces, bottom→top, WITHOUT a
  /// roof — complete from placement (the level never changes it).
  /// `variantSeed` (0..2, from the tower cell) styles the battery
  /// and bunker detailing; deck/keep letters come from the def.
  let stackFor (def: TowerDef) (variantSeed: int) : ModelInfo[] =
    let v = variantSeed % 3

    match def.Chassis with
    | Chassis.Emplacement -> emplacementStack
    | Chassis.Deck letter -> deckStacks[(letter % 3 + 3) % 3]
    | Chassis.Battery -> batteryStacks[v]
    | Chassis.Bunker -> bunkerStacks[v]
    | Chassis.Keep letter -> keepStack letter

  /// Sum of the stack pieces's SizeY (unscaled). Variant-independent
  /// (tier variants share SizeY), so the canonical variant 0 is used.
  let stackHeight(def: TowerDef) : float32 =
    let pieces =
      match def.Chassis with
      | Chassis.Emplacement -> emplacementStack
      | Chassis.Deck letter -> deckStacks[(letter % 3 + 3) % 3]
      | Chassis.Battery -> batteryStacks[0]
      | Chassis.Bunker -> bunkerStacks[0]
      | Chassis.Keep letter -> keepStack letter

    pieces |> Array.sumBy(fun piece -> piece.SizeY)

  /// The tile top — tower bodies sit on it (the ground tiles are
  /// bottom-anchored with their top surface at y = 0.2).
  let baseY = 0.2f

  /// The gun MOUNT height for gun-carrying chassis: the wooden
  /// mount's top (emplacement — pad + wood-structure), the bay floor
  /// (bunker — the bottom's top, the gun sits inside the bay) or the
  /// stack top (battery). Decks and keeps are self-armed — the mount
  /// equals the stack top (no gun model is drawn).
  let weaponY(def: TowerDef) : float32 =
    match def.Chassis with
    | Chassis.Emplacement -> baseY + stackHeight def * towerScale
    | Chassis.Bunker -> baseY + squareBottoms[0].SizeY * towerScale
    | _ -> baseY + stackHeight def * towerScale

  /// The tower body's top — the HUD Lv-tag anchor.
  let towerTop(def: TowerDef) : float32 = baseY + stackHeight def * towerScale

  /// The weapon model's scaled height (0 when the def carries no gun
  /// model — decks/keeps).
  let gunHeight(def: TowerDef) : float32 =
    def.WeaponModel
    |> ValueOption.map(fun g -> g.SizeY * towerScale * def.GunScale)
    |> ValueOption.defaultValue 0f

  /// The APPROXIMATE muzzle HEIGHT shots spawn at:
  ///   emplacement/battery — 75 % up the mounted gun;
  ///   bunker — 60 % up the enclosed gun (pokes from the bay);
  ///   deck — the gun deck's top (embrasure level);
  ///   keep — the crown.
  let muzzleY(def: TowerDef) : float32 =
    match def.Chassis with
    | Chassis.Bunker -> weaponY def + gunHeight def * 0.6f
    | Chassis.Deck _ ->
      // The deck (middle) top: bottom + middle rises above the tile.
      baseY + (roundBottoms[0].SizeY + roundMiddles[0].SizeY) * towerScale
    | Chassis.Keep _ -> towerTop def
    | _ -> weaponY def + gunHeight def * 0.75f

  /// The muzzle's FORWARD REACH (unscaled model units) from the
  /// tower center along the firing line — where the shot and the
  /// muzzle VFX actually spawn:
  ///   gun mounts — the gun model's barrel half-length (scaled by
  ///     GunScale, so big guns reach further);
  ///   decks/keeps — the tower's edge (the embrasure/opening).
  let muzzleReach(def: TowerDef) : float32 =
    match def.Chassis, def.WeaponModel with
    | Chassis.Deck _, _
    | Chassis.Keep _, _ -> 0.5f
    | _, ValueSome gun -> gun.SizeZ * 0.5f * def.GunScale
    | _, ValueNone -> 0.5f
