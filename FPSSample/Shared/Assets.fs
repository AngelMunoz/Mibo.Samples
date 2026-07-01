namespace FPSSample

open System

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
  let blasterE = blasterPath + "blaster-e.glb"
  let blasterF = blasterPath + "blaster-f.glb"
  let blasterG = blasterPath + "blaster-g.glb"
  let blasterH = blasterPath + "blaster-h.glb"
  let blasterI = blasterPath + "blaster-i.glb"
  let blasterJ = blasterPath + "blaster-j.glb"
  let blasterK = blasterPath + "blaster-k.glb"
  let blasterL = blasterPath + "blaster-l.glb"
  let blasterM = blasterPath + "blaster-m.glb"
  let blasterN = blasterPath + "blaster-n.glb"
  let blasterO = blasterPath + "blaster-o.glb"
  let blasterP = blasterPath + "blaster-p.glb"
  let blasterQ = blasterPath + "blaster-q.glb"
  let blasterR = blasterPath + "blaster-r.glb"

  let bulletFoamThick = blasterPath + "bullet-foam-thick.glb"
  let bulletFoamTipThick = blasterPath + "bullet-foam-tip-thick.glb"
  let bulletFoamTip = blasterPath + "bullet-foam-tip.glb"
  let bulletFoam = blasterPath + "bullet-foam.glb"

  // ── Effects ─────────────────────────────────────────────────────────────────
  let smoke = blasterPath + "smoke.glb"

  // ── Gun Sounds ───────────────────────────────────────────────────────────────
  let gunSounds = "assets/gun_sounds/7.62x39/"

  let gunSoundSingle = gunSounds + "762x39 Single MP3.mp3"
  let gunSoundDoubleTap = gunSounds + "762x39 Double Tap MP3.mp3"
  let gunSoundBurst = gunSounds + "762x39 Burst MP3.mp3"
  let gunSoundSpray = gunSounds + "762x39 Spray MP3.mp3"

  let reloadSounds = "assets/gun_sounds/reloads/"

  let reloadFast = reloadSounds + "reload-fast.mp3"
  let reloadRifle = reloadSounds + "reload-rifle.mp3"
  let reloadHeavy = reloadSounds + "reload-heavy.mp3"

  // ── Horror SFX ──────────────────────────────────────────────────────────────
  let horrorSfx = "assets/horror_sfx/"

  let bite = horrorSfx + "Bite.wav"
  let childLaugh = horrorSfx + "Child laugh.wav"
  let footstepsRunning = horrorSfx + "Footsteps_ running.wav"
  let footstepsWalking = horrorSfx + "Footsteps_walking.wav"
  let gasp = horrorSfx + "Gasp_3.wav"
  let injured = horrorSfx + "Injured.wav"
  let breathingFast = horrorSfx + "Breathing_fast.wav"
  let laughSpooky = horrorSfx + "Laugh_spooky_4.wav"

  let roboticSounds = [|
    horrorSfx + "Robotic_bass.wav"
    horrorSfx + "robotic_groan_3.wav"
    horrorSfx + "robotic_hiss.wav"
    horrorSfx + "Scream_Robotic.wav"
  |]

  /// Picks a random robotic sound from the pool.
  let roboticSound(rng: Random) : string =
    roboticSounds[rng.Next(roboticSounds.Length)]

  /// Weapon archetype inferred from the blaster preview shape.
  [<Struct; RequireQualifiedAccess>]
  type WeaponClass =
    | Pistol
    | Smg
    | Rifle
    | Heavy

  /// Maps a blaster model path to a weapon class (visual archetype).
  let weaponClass(path: string) : WeaponClass =
    let name =
      System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant()

    match name with
    | "blaster-b"
    | "blaster-c"
    | "blaster-j"
    | "blaster-k"
    | "blaster-o"
    | "blaster-r" -> WeaponClass.Pistol
    | "blaster-a"
    | "blaster-e"
    | "blaster-f"
    | "blaster-i" -> WeaponClass.Smg
    | "blaster-n"
    | "blaster-d"
    | "blaster-g"
    | "blaster-m"
    | "blaster-p" -> WeaponClass.Rifle
    | "blaster-q"
    | "blaster-h"
    | "blaster-l" -> WeaponClass.Heavy
    | _ -> WeaponClass.Rifle

  /// Fire cooldown (seconds) for each weapon class.
  let fireCooldown(wc: WeaponClass) : float32 =
    match wc with
    | WeaponClass.Pistol -> 0.35f
    | WeaponClass.Smg -> 0.05f
    | WeaponClass.Rifle -> 0.10f
    | WeaponClass.Heavy -> 0.5f

  /// Fire sound profile for each weapon class.
  let gunSound(wc: WeaponClass) : string =
    match wc with
    | WeaponClass.Pistol -> gunSoundSingle
    | WeaponClass.Smg -> gunSoundSingle
    | WeaponClass.Rifle -> gunSoundSingle
    | WeaponClass.Heavy -> gunSoundSingle

  /// Reload sound profile for each weapon class.
  let reloadSound(wc: WeaponClass) : string =
    match wc with
    | WeaponClass.Pistol -> reloadFast
    | WeaponClass.Smg -> reloadRifle
    | WeaponClass.Rifle -> reloadRifle
    | WeaponClass.Heavy -> reloadHeavy
