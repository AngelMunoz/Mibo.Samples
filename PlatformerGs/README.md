# PlatformerGs — the Platformer sample in G#

A port of [Platformer/Raylib](../Platformer/Raylib) to [G#](https://davidobando.github.io/gsharp/),
running on the Mibo framework with the **Adaptive architecture**
(`AdaptiveProgram` + `AdaptiveRaylibGame`) instead of the MVU/Elmish loop the
original uses — same choice as [Defli](../Defli). Single project, single
backend; all game logic lives here, there is no shared library.

## Build & run

```sh
dotnet run --project PlatformerGs/PlatformerGs.gsproj
```

## Layout

| File | Ported from | Contents |
|---|---|---|
| `Types.gs` | Shared/Constants.fs, Types.fs | constants, tile/biome/collider enums, chunk + world state, `RenderFrame` |
| `Tiles.gs` | Shared/TileData.fs, TileAnimations.fs | Kenney atlas positions, per-tile sprite + collider lookup, animated tile defs |
| `WorldGen.gs` | Shared/WorldGen.fs, Stamps.fs | value-noise biomes/elevation, jump-reachability clamps, ground/platform planning, extraction, chunk streaming |
| `Sim.gs` | Shared/Physics.fs, Particles.fs, DayNight.fs, Raylib/Systems.fs, Camera.fs | the adaptive **Update phase**: input → physics → streaming → particles → day/night → animation → camera |
| `View.gs` | Raylib/View.fs | the view: sky gradient, camera, sun/moon/torch lighting with occluders, lit tiles, player, particles, HUD |
| `Program.gs` | Raylib/Program.fs | asset loading, `AdaptiveProgram` wiring, `AdaptiveRaylibGame` host |

## Adaptive architecture mapping

- **State** — one mutable `World` object (the `StateCell` role).
- **Init** — loads assets, creates the `LightContext2D`/camera/sprites, preloads
  spawn chunks, returns `AdaptiveInit.ofFrameBuilder`.
- **Frame force** — packs a `RenderFrame` struct each Step; the renderer reads
  it between Steps.
- **Update** — `stepWorld` runs the whole sim once per frame and mutates the
  world directly; there is no Msg/Cmd — the adaptive program's intent queue is
  not needed because nothing here defers work across steps.

## Deliberate deviations from the F# sample

- **No `Mibo.Layout`** — chunk tile grids are a plain `Dictionary<int32, TileV>`
  (only stamped cells exist). Physics still uses the extracted collider arrays.
- **Chunk generation is synchronous** with a 2-chunks-per-step budget instead of
  `Cmd.ofAsync`.
- **Minimap and the Diagnostics module are not ported**; the HUD shows
  `Raylib.GetFPS()` instead.
- **Assets/input bypass the service locator** (see Program.gs): Mibo exposes
  services only through `GameContext.getService<'T>`, whose type parameter
  appears in return position only — G# 0.4.273's overload applicability is
  argument-directed and cannot select it. Textures/font/sound load via raylib
  directly and input polls `Raylib.IsKeyDown` in the Update phase.

## G# ↔ F# interop notes (empirical, GSharp.NET.Sdk 0.4.273)

- `ProjectReference` to F# projects works; transitive refs flow.
- F# function values: `FuncConvert.FromFunc(lambda)` / `FSharpFunc[A,B].FromConverter`;
  curried multi-arg functions become **nested** `FSharpFunc`s. F# `unit` return → `default(Unit)`.
- F# modules with a same-named type emit as `XxxModule`; without a clash the
  bare module name is the class (`Texture.filter`).
- The 2D draw DSL takes **neutral `Mibo.Color`** (not `Raylib_cs.Color`);
  `int<RenderLayer>` layers are erased to plain ints.
- Concrete-signature `let inline` functions ARE emitted and callable (arg order
  = source curried order); SRTP-only inline functions and type abbreviations
  (e.g. `SubId = string<subId>`) are erased — pass the underlying type.
- Return-only generic methods (`getService<'T>`) are **not callable** from G#
  yet; `getVar x T = ...` typed declarations, explicit type args, and target
  typing all fail. Framework feedback: expose concrete-signature accessors.
- Enums are comma-separated; no nested `func` declarations; no static class
  members; no int→float widening; raylib `CBool` converts via `bool(x)`;
  interpolated strings are sigil-free (`"${expr}"`).
- **F#'s `>>>` on signed int32 sign-propagates** (arithmetic), while G#'s
  `>>>` zero-fills — the value-noise `hash01` needed plain `>>` for parity.
- **Parity harness**: `GS_DUMP=1 PlatformerGs.exe` dumps every generated tile
  for a grid of chunks and seeds in the same format as an `fsi` dump of
  `Platformer.WorldGen.generateChunk`; the two outputs are diffed in CI-style
  by hand. As of this writing 42 chunk generations across 2 seeds are
  tile-identical (3,744 tiles, kinds + biomes + positions).
