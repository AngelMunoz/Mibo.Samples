namespace Defli.MonoGame

/// <summary>
/// MonoGame's pixel-space rectangle (int-based). The sim carries float
/// coordinates; conversion happens at this view edge.
/// </summary>
type Rectangle = Microsoft.Xna.Framework.Rectangle

/// <summary>XNA conversions for the few records that store XNA Vector2
/// (SpriteState.Origin, Camera2D, Lighting.Particle2D) — the fluent draw
/// surface itself takes System.Numerics.Vector2.</summary>
module Xna =

  let inline v2(v: System.Numerics.Vector2) =
    Microsoft.Xna.Framework.Vector2(v.X, v.Y)

/// <summary>
/// XNB asset names for the MonoGame content pipeline — no extension, resolved
/// through <c>IAssets</c> (ContentManager) relative to the <c>Content</c> output
/// dir. The raylib client loads the same files as loose paths WITH extensions;
/// the .mgcb names its assets to mirror these paths (see Content/Content.mgcb).
/// </summary>
module Paths =

  [<Literal>]
  let Sheet = "kenney_tower-defense-top-down/towerDefense_tilesheet"

  [<Literal>]
  let Impact = "kenney_particle_pack/spark_01"

  [<Literal>]
  let Explosion = "kenney_smoke_particles/Explosion/explosion03"

  [<Literal>]
  let DeathPoof = "kenney_smoke_particles/Black smoke/blackSmoke05"

  [<Literal>]
  let Muzzle = "kenney_smoke_particles/Flash/flash00"

  [<Literal>]
  let Placement = "kenney_particle_pack/dirt_01"

  [<Literal>]
  let BaseHit = "kenney_smoke_particles/Black smoke/blackSmoke05"

  [<Literal>]
  let Font = "Fonts/Monogram"
