namespace FPSSample

/// Logical asset paths shared across backends.
/// The actual files live under <c>FPSSample/assets/kenney_platformer-kit/Models/</c>.
/// Each backend resolves these logical paths to its own loading mechanism
/// (raylib loads at runtime; MonoGame uses the content pipeline).
module Assets =

  let basePath = "assets/kenney_platformer-kit/Models/"

  // ── Characters (used for enemies) ──────────────────────────────────────────
  let characterOobi = basePath + "character-oobi.glb"
  let characterOodi = basePath + "character-oodi.glb"
  let characterOoli = basePath + "character-ooli.glb"
  let characterOopi = basePath + "character-oopi.glb"
  let characterOozi = basePath + "character-oozi.glb"

  /// All available character model paths, cycled across enemies for variety.
  let characters = [|
    characterOobi
    characterOodi
    characterOoli
    characterOopi
    characterOozi
  |]

  /// Picks a character path by index, wrapping around.
  let character(i: int) = characters[i % characters.Length]

  // ── Terrain / blocks (used for walls and cover) ────────────────────────────
  let blockGrass = basePath + "block-grass.glb"
  let blockGrassLarge = basePath + "block-grass-large.glb"
  let blockGrassSlope = basePath + "block-grass-large-slope.glb"
  let platformRamp = basePath + "platform-ramp.glb"

  // ── Pickups ────────────────────────────────────────────────────────────────
  let heart = basePath + "heart.glb"
  let coinGold = basePath + "coin-gold.glb"
  let star = basePath + "star.glb"

  // ── Crates / props ─────────────────────────────────────────────────────────
  let crate = basePath + "crate.glb"
  let barrel = basePath + "barrel.glb"
  let chest = basePath + "chest.glb"

  // ── Blaster kit (weapons) ──────────────────────────────────────────────────
  let blasterPath = "assets/kenney_blaster-kit/Models/"

  let blasterA = blasterPath + "blaster-a.glb"
  let blasterB = blasterPath + "blaster-b.glb"
  let blasterC = blasterPath + "blaster-c.glb"
  let blasterD = blasterPath + "blaster-d.glb"

  let bulletFoamThick = blasterPath + "bullet-foam-thick.glb"
  let bulletFoamTipThick = blasterPath + "bullet-foam-tip-thick.glb"
  let bulletFoamTip = blasterPath + "bullet-foam-tip.glb"
  let bulletFoam = blasterPath + "bullet-foam.glb"
