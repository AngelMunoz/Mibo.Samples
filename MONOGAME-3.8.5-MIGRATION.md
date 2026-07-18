# MonoGame 3.8.5 Migration — Plan & Findings

> Status as of 2026-07-17. Part 1 (version bump) is implemented on framework branch
> `feat/monogame-3.8.5` (in the `Mibo/` submodule) and samples branch
> `feat/monogame-3.8.5` (this repo). Parts 2 & 3 are **feasibility evaluations** — no
> shader/template code has been changed; this document captures findings and the path forward.
>
> Scope (per request): Part 1 is a concrete migration; Parts 2 & 3 produce findings +
> recommended steps before any code change.

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
- `~/repos/MonoGame` — checkout `10373f33b` (5 commits past tag `v3.8.5`).
- `~/repos/Kipo/Pomo.Vulkan` — a working MonoGame 3.8.5 Vulkan client (multi-backend solution:
  `Pomo.Core` shared lib + `Pomo.{DesktopGL,WindowsDX,Vulkan}` clients).

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

## Part 2 — Shaders → Vulkan & DirectX12 (evaluation)

**Verdict: feasible, but neither is drop-in. DX12 is the lower-risk target; Vulkan is
higher-value (cross-platform) but has one serious, shader-specific risk that must be
validated before committing to ship it.**

### What MonoGame 3.8.5 provides (from `Tools/MonoGame.Effect.Compiler/Effect/`)
mgfxc now has **four** profiles (`ShaderProfile.cs:30-36`):

| Profile       | Shader model      | Toolchain                  | Macros defined   |
|---------------|-------------------|----------------------------|------------------|
| `OpenGL`      | `vs_3_0`/`ps_3_0` | fxc (needs Wine on Linux)  | —                |
| `DirectX_11`  | `vs_5_0`/`ps_5_0` | fxc (needs Wine on Linux)  | `HLSL`           |
| **`DirectX_12`** | **`vs_6_0`/`ps_6_0`** | **DXC** (`MonoGame.Tool.Dxc`) | `HLSL`, `SM6` |
| **`Vulkan`**     | **`vs_6_0`/`ps_6_0`** | **DXC → SPIR-V**             | `VULKAN`, `SM6` |

- Platform auto-mapping (`ShaderProfile.cs:81-88`): `DesktopVK → Vulkan`,
  `WindowsDX12 → DirectX_12`.
- DXC ships cross-platform via the `MonoGame.Tool.Dxc` NuGet (bundled with `dotnet-mgfxc`).
  So the Vulkan/DX12 path is **cleaner on Linux than legacy DX11** (DX11 still needs Wine via
  `WineHelper.cs`). And it is **build-time only** — Mibo keeps embedding prebuilt `.mgfx`.
- `GraphicsBackend.{DirectX12,Vulkan}` + `PlatformInfo.GraphicsBackend` already exist in
  3.8.4.1's Native package. `ShaderLoader.fs:34-40` **already branches** on them — DX12
  currently (wrongly) reuses `.dx.mgfx`, Vulkan currently `failwith`s.

### Required work (concrete, sequenced)
1. **SM6 shader-model branch in all 5 `.fx`.** Today they use
   `#if OPENGL vs_3_0 … #else vs_5_0`. Both new profiles define `SM6`, so one branch covers
   both: `#if OPENGL …vs_3_0… #elif defined(SM6) …vs_6_0… #else …vs_5_0…`.
2. **`Shaders/script.fsx`** — add two `mgfxc` invocations per shader:
   `/Profile:Vulkan → .vk.mgfx` and `/Profile:DirectX_12 → .dx12.mgfx`. Requires
   `dotnet-mgfxc 3.8.5` (the cache here had only 3.8.4.1; 3.8.5 is on nuget).
3. **`Mibo.MonoGame.fsproj`** — `<EmbeddedResource>` the new `.vk.mgfx` / `.dx12.mgfx`.
4. **`ShaderLoader.fs`** — fix routing: `Vulkan → .vk.mgfx`, `DirectX12 → .dx12.mgfx`
   (stop reusing the DX11 variant for DX12).

### Risks / unknowns — *these are exactly what the deferred empirical compile would confirm*
1. **Vulkan single-cbuffer limit — HIGHEST RISK.** `VulkanShaderProfile` throws if an effect
   has "more than one constant buffer (cbuffer) structures" (`ShaderProfile.Vulkan.cs:259-263`).
   Mibo's loose globals pack into one implicit cbuffer (count is fine), **but the arrays are
   large**: `ForwardPbr.fx` and `DepthShadow.fx` carry `boneMatrices[128]` (≈8 KB) and
   `shadowViewProjs[16]` (≈1 KB), plus point/spot-light arrays. Vulkan UBO(dynamic) has a
   guaranteed-min size (commonly 16 KB, device-dependent) — `boneMatrices` alone may push the
   single cbuffer over. mgfxc's Vulkan path offers **no storage-buffer escape hatch**, so the
   likely mitigation is capping `MAX_BONES` / `MAX_SHADOW_CASTERS` or restructuring — an
   API-facing change. **Validate this before promising Vulkan.**
2. **DX12 sampler mapping.** The `.fx` use legacy combined `sampler2D texture0 : register(s0)`.
   The DX12 profile builds a root signature with **separate** SRV(t)/Sampler(s) descriptor
   tables and parses textures/samplers independently (`ShaderProfile.DirectX12.cs:281-411`).
   The combined `sampler2D` syntax maps cleanly to Vulkan (combined image-sampler) but may
   not to DX12's split model — a DX12-specific porting item.
3. **DXC strictness.** DXC (SM6.0) is stricter than fxc (SM5.0). The current SM3.0 workarounds
   (`tex2Dlod`, `[loop]`+`break`, manual PCF, `[unroll]`) are valid under SM6.0, but the
   `#else` SM5.0 path must not silently gate the SM6 path. Expect minor HLSL fixes; magnitude
   unknown until compile.
4. **Instancing vertex layout.** `Instanced.fx`/`ForwardPbr.fx` read the per-instance matrix
   from `TEXCOORD1..4` (dual stream). The profile source notes `location`/`name` are
   "unused at runtime under the new native backends" — instancing binding under the native
   backends needs confirmation.

### Recommendation
- **Target DX12 first** (no single-cbuffer cap, HLSL-native, closest to existing DX11 HLSL).
  **Vulkan second** (cross-platform value), gated on risk #1.
- Sequence: add the `SM6` branch → get `dotnet-mgfxc 3.8.5` → compile the **simplest** shader
  first (DepthShadow *without* skinning, or LitSprite) → then the full set → tackle
  `ForwardPbr`/`DepthShadow` large arrays last.
- **Do not commit to shipping Vulkan until risk #1 is empirically answered.**

---

## Part 3 — Templates → Vulkan & DirectX12 (evaluation)

**Verdict: feasible and lower-risk than Part 2 — but it depends on Part 2. Vulkan is the
valuable add; DX12 is Windows-only and can't be validated on this Linux box.**

### What MonoGame 3.8.5 provides (the new native model, proven by `Pomo.Vulkan.fsproj`)
A Vulkan client is just: `<MonoGamePlatform>DesktopVK</MonoGamePlatform>` +
`MonoGame.Framework.Native` (already in the shared `src/` lib) + per-OS native runtimes
`MonoGame.Runtime.{Windows,Linux,Mac}.Vulkan` + `MonoGame.Content.Builder.Task`, all `3.8.*`.
Cross-platform, net10.0. (DX12 equivalent: `MonoGame.Runtime.Windows.DX12` +
`MonoGamePlatform=WindowsDX12`, Windows-only.)

### Required work
1. Add a `Vulkan/` client to each template, mirroring `Pomo.Vulkan.fsproj` (a 3-line
   `Program.fs` = `create()` + `Run()`; fsproj with `DesktopVK` + the three
   `Runtime.*.Vulkan` refs).
2. Content: the shared `Content.mgcb` currently uses `/platform:DesktopGL`; a Vulkan client
   needs `/platform:DesktopVK` so effects compile to the Vulkan profile. **`Pomo.Vulkan`
   sidesteps this by using only `BasicEffect` (no custom shaders) — Mibo's templates can't**,
   because they wire the Forward PBR / LitSprite pipelines.
3. `template.json`: today it's a single identity with no backend choice. Decide UX — recommend
   a `--backend` choice symbol (`desktopgl|windowsdx|vulkan`) so one template identity serves
   all, rather than forking the template per backend.
4. (Optional) `WindowsDX12/` client — Windows-only, untestable here, marginal value over the
   existing DX11 `WindowsDX`.

### Risks / coupling
- **Templates depend on Part 2.** The 3D template renders via `ForwardPbrPipeline` (custom
  shaders) and the 2D template via `LitSprite`. A Vulkan template client needs
  Vulkan-compiled PBR/LitSprite `.mgfx` to actually render — so Part 3 can't ship Vulkan
  until Part 2's shaders exist. This is the dominant constraint.
- **mgcb 3.8.5 + DXC** required for `/platform:DesktopVK` effect compilation in the content
  pipeline.
- DX12 template is Windows-only — defer/skip.

### Recommendation
- Ship a **Vulkan** client variant (cross-platform value), **after Part 2 lands Vulkan
  shaders**.
- Use a `--backend` template symbol rather than separate template identities.
- **Skip DX12** for templates (Windows-only, untestable here, little gain over DX11
  `WindowsDX`).

---

## Overall sequencing

1. ✅ **Part 1** (done) — framework on 3.8.5. Then bump samples' MonoGame host packages and
   update the `Mibo/` submodule pointer → framework PR first, then samples PR.
2. **Part 2a** — DX12 shaders (lower risk): `SM6` branch → `dotnet-mgfxc 3.8.5` →
   compile/validate on Windows.
3. **Part 2b** — Vulkan shaders: validate the **single-cbuffer / `boneMatrices` UBO-size**
   risk first; proceed only if it clears.
4. **Part 3** — Vulkan template clients (gated on 2b), with a `--backend` symbol.

**The single decision that most affects scope: Part 2 risk #1** (Vulkan single-cbuffer vs
Mibo's large uniform arrays). Everything else is straightforward porting.

---

## Windows validation checklist (next steps)

1. Checkout framework branch `feat/monogame-3.8.5` in the `Mibo/` submodule; confirm it
   builds (`dotnet build Mibo.slnx`) and `Mibo.MonoGame.Tests` passes.
2. Checkout samples branch `feat/monogame-3.8.5`; bump the samples' MonoGame host packages
   (`MonoGame.Framework.DesktopGL`/`WindowsDX`/`Content.Builder.Task`) `3.8.4.1 → 3.8.5`
   across the MonoGame sample clients; update the `Mibo/` submodule pointer to the framework
   commit.
3. Build + run a MonoGame sample (e.g. `FPSSample/MonoDesktop` or `MonoPlatformer`) on
   DesktopGL and WindowsDX to confirm the 3.8.5 runtime works visually.
4. For Part 2: `dotnet tool install -g dotnet-mgfxc --version 3.8.5`, then try compiling the
   simplest shader to `/Profile:DirectX_12` and `/Profile:Vulkan` to surface the real errors
   (risks #1–#4 above) before writing the SM6 branch.
