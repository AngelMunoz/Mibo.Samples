# MonoGame 3.8.5 Migration — Plan & Findings

> Status as of 2026-07-17. Part 1 (version bump) is implemented on framework branch
> `feat/monogame-3.8.5` (in the `Mibo/` submodule) and samples branch
> `feat/monogame-3.8.5` (this repo). Part 2 (shaders → Vulkan & DirectX12) is **DONE** —
> all 5 shaders compile for all 4 profiles, 20 `.mgfx` variants are embedded, framework
> builds clean, 14/14 tests pass. Part 3 (sample migration) is the next phase.

## Baseline (before this work)

- `Mibo.MonoGame` + `Mibo.MonoGame.Tests` referenced `MonoGame.Framework.Native` **3.8.4.1**
  (`net10.0;net8.0`).
- 5 MonoGame HLSL shaders (`LitSprite`, `LitSpriteNormalMap`, `Instanced`, `ForwardPbr`,
  `DepthShadow`) compiled at **build time** by `Mibo/src/Mibo.MonoGame/Shaders/script.fsx`
  into `.dx.mgfx` (DirectX 11, SM5.0) + `.ogl.mgfx` (OpenGL, SM3.0), embedded as manifest
  resources, and selected at runtime by `Mibo.MonoGame/Graphics2D/ShaderLoader.fs`.
  **Mibo pre-compiles its shaders — no tooling (mgfxc/DXC) runs at runtime.** End users ship
  only the embedded `.mgfx` bytes.
- Templates `mibo-mg-2d`/`mibo-mg-3d` ship a shared `src/` lib + `DesktopGL/` (OpenGL) and
  `WindowsDX/` (DX11) thin clients.

### Reference material used (source-only, no DLL inspection / no reflection / no Python)

- `E:\MonoGame` — checkout tag `v3.8.5`.

---

## Part 1 — Migrate `Mibo.MonoGame` to 3.8.5 ✅ DONE (framework side)

### What changed

- `Mibo/src/Mibo.MonoGame/Mibo.MonoGame.fsproj`: `MonoGame.Framework.Native 3.8.4.1 → 3.8.5`
- `Mibo/src/Mibo.MonoGame.Tests/Mibo.MonoGame.Tests.fsproj`: same bump
- `Mibo/CHANGELOG.md`: `### Changed` entry under `[Unreleased]`

### Validation performed (on this Linux box, source-only)

- Framework solution `Mibo.slnx` builds clean (0 errors; only pre-existing harmless
  `CNG0002` KeepAChangelog warnings).
- `Mibo.MonoGame.Tests`: **14/14 pass** on 3.8.5.
- Downstream consumer `FPSSample/MonoShared` (ProjectReferences the submodule) builds clean —
  NuGet unified `MonoGame.Framework.Native` up to 3.8.5 (no NU1605/NU1608 conflicts).
- `dotnet fantomas .` clean (155 unchanged, 0 errored).

### Remaining for PR-readiness (samples side — do on Windows)

The samples' own MonoGame host packages still pin **3.8.4.1** and should move to **3.8.5**
for runtime consistency:

- `FPSSample/MonoShared/MonoShared.fsproj`: `MonoGame.Framework.Native 3.8.4.1`
- `FPSSample/MonoDesktop/MonoDesktop.fsproj`: `MonoGame.Framework.DesktopGL` +
  `MonoGame.Content.Builder.Task` @ 3.8.4.1
- Other MonoGame sample clients (`MonoPlatformer`, `MonoThreeD`, `FPSSample/MonoWindowsDX`,
  `PingPong` MonoGame client) — same bump.
  After bumping, also update the `Mibo/` submodule pointer on the samples branch to the
  framework branch's pushed commit (ties the two branches together).

---

## Part 2 — Shaders → Vulkan & DirectX12 ✅ DONE

**Result: all 5 shaders compile for all 4 profiles (OpenGL, DirectX 11, DirectX 12,
Vulkan). 20 `.mgfx` variants embedded. Framework builds clean, 14/14 tests pass.**

### What was done

1. **SM6 shader-model branch.** Added `#elif defined(SM6)` with `vs_6_0`/`ps_6_0` to all
   five `.fx` files. Both DX12 and Vulkan profiles define `SM6`, so one branch covers both.
2. **SM6 sampler compatibility layer.** DXC (SM6.0) dropped the legacy `sampler2D` type and
   `tex2D`/`tex2Dlod` intrinsics. Added a preprocessor block (`#if defined(SM6)`) that
   declares `Texture2D` + `SamplerState` pairs and redefines `tex2D`/`tex2Dlod` as macros
   using `.Sample()`/`.SampleLevel()`. Non-SM6 paths keep the original `sampler2D` syntax
   unchanged. The `shadowAtlas` sampler-state initializer (Point/Clamp) is kept for SM3/SM5
   and dropped for SM6 (defaults apply; runtime sampler-state setting is a follow-up).
3. **Semantic fixes.** The 2D shaders (`LitSprite`, `LitSpriteNormalMap`, `Instanced`) used
   `POSITION0` for the VS clip-space output and `COLOR0` for the PS return value. SM6.0
   requires `SV_POSITION` and `SV_TARGET` respectively. Changed all profiles unconditionally
   (the 3D shaders already used these system-value semantics on all profiles).
4. **`script.fsx`** — added `DirectX12` and `Vulkan` mgfxc invocations producing `.dx12.mgfx`
   and `.vk.mgfx` per shader.
5. **`Mibo.MonoGame.fsproj`** — embedded the 10 new `.mgfx` resources.
6. **`ShaderLoader.fs`** — fixed routing: `DirectX12 → .dx12.mgfx`, `Vulkan → .vk.mgfx`.
   DX12 no longer (incorrectly) reuses the DX11 variant.

### What MonoGame 3.8.5 provides (from `Tools/MonoGame.Effect.Compiler/Effect/`)

mgfxc now has **four** profiles (`ShaderProfile.cs:30-36`):

| Profile          | Shader model          | Toolchain                     | Macros defined  |
| ---------------- | --------------------- | ----------------------------- | --------------- |
| `OpenGL`         | `vs_3_0`/`ps_3_0`     | fxc (needs Wine on Linux)     | —               |
| `DirectX_11`     | `vs_5_0`/`ps_5_0`     | fxc (needs Wine on Linux)     | `HLSL`          |
| **`DirectX_12`** | **`vs_6_0`/`ps_6_0`** | **DXC** (`MonoGame.Tool.Dxc`) | `HLSL`, `SM6`   |
| **`Vulkan`**     | **`vs_6_0`/`ps_6_0`** | **DXC → SPIR-V**              | `VULKAN`, `SM6` |

- Platform auto-mapping (`ShaderProfile.cs:84-91`): `DesktopVK → Vulkan`,
  `WindowsDX12 → DirectX_12`.
- DXC ships cross-platform via the `MonoGame.Tool.Dxc` NuGet (bundled with `dotnet-mgfxc`).
  It is **build-time only** — Mibo keeps embedding prebuilt `.mgfx`.

### Risks resolved by the empirical compile

1. **Vulkan single-cbuffer limit — RESOLVED.** `VulkanShaderProfile` throws if an effect has
   more than one cbuffer structure. All 5 shaders have a single implicit cbuffer from loose
   globals — the compile passed. The large arrays (`boneMatrices[128]`, `shadowViewProjs[16]`,
   point/spot-light arrays) did **not** cause a compile-time failure. Runtime UBO size limits
   remain device-dependent but compilation succeeds; this will be validated when samples run
   on Vulkan hardware.
2. **DX12/Vulkan sampler mapping — RESOLVED.** The legacy combined `sampler2D` syntax was
   incompatible with DXC. Resolved by splitting into `Texture2D` (t-register) + `SamplerState`
   (s-register) pairs for SM6. The DX12 root signature and Vulkan reflection both expect this
   split model. Call sites are unchanged via macros.
3. **DXC strictness — RESOLVED.** `POSITION0`/`COLOR0` output semantics were rejected by DXC.
   Changed to `SV_POSITION`/`SV_TARGET`. All `[loop]`/`[unroll]`/`tex2Dlod` patterns compiled
   cleanly under SM6.0.
4. **Instancing vertex layout — COMPILES, runtime untested.** `Instanced.fx`/`ForwardPbr.fx`
   read the per-instance matrix from `TEXCOORD1..4`. Compilation succeeds; runtime binding
   under native backends will be validated during the sample migration.

### Follow-up items (not blocking)

- **Shadow atlas sampler state on SM6.** The Point-filter/Clamp sampler-state on `shadowAtlas`
  is currently dropped for SM6 profiles (defaults apply). Runtime `SamplerState` configuration
  via the effect API is a follow-up if shadow artifacts appear.
- **PBR pipeline improvements.** Deferred to a future attempt per scope.

---

## Part 3 — Templates → DirectX12 & Vulkan (pending — sample migration phase)

**Goal: migrate the MonoGame sample projects to 3.8.5 and switch to Vulkan/DX12 where
applicable.** The framework shader work (Part 2) is the prerequisite and is now complete.

### Reference projects for fsproj structure

From `E:\Kipo` (Pomo — a real MonoGame Vulkan/DX12 consumer) and `E:\MonoGame.Templates`:

**Vulkan (DesktopVK) thin client** — key fsproj properties:
```xml
<PropertyGroup>
  <MonoGamePlatform>DesktopVK</MonoGamePlatform>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MonoGame.Framework.Native" Version="3.8.5" />
  <PackageReference Include="MonoGame.Runtime.Windows.Vulkan" Version="3.8.5" />
  <PackageReference Include="MonoGame.Runtime.Mac.Vulkan" Version="3.8.5" />
  <PackageReference Include="MonoGame.Runtime.Linux.Vulkan" Version="3.8.5" />
  <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.*" />
</ItemGroup>
```

**DirectX 12 (WindowsDX12) thin client** — key csproj properties:
```xml
<PropertyGroup>
  <MonoGamePlatform>WindowsDX12</MonoGamePlatform>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MonoGame.Framework.Native" Version="3.8.5" />
  <PackageReference Include="MonoGame.Runtime.Windows.DX12" Version="3.8.5" />
  <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.*" />
</ItemGroup>
```

### Samples to migrate

- `MonoPlatformer`, `MonoThreeD` — add `DesktopVK` / `WindowsDX12` thin client projects
  alongside existing `DesktopGL` / `WindowsDX`.
- `FPSSample/MonoDesktop`, `FPSSample/MonoWindowsDX` — add Vulkan/DX12 client variants.
- `PingPong` MonoGame client — same pattern.
- Bump all `MonoGame.Framework.*` packages from 3.8.4.1 → 3.8.5.

---
