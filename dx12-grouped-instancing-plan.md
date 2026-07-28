# Split DX12 skinned-instanced shaders into separate .fx files

## Problem

Mibo's `ForwardPbr.fx` (8 techniques, large `$Globals` with light arrays + `bonePaletteGroup[128]`) compiles cleanly for DX11/Vulkan/OpenGL but the DX12 mgfx reflection parser (`ShaderProfile.DirectX12.cs`) drops `bonePaletteGroup`, `groupBoneCount`, and `paletteTexSize` from the compiled effect. At runtime these params are null, uploads silently no-op, and the grouped-uniform technique renders nothing. VTF also crashes on DX12 (`NotSupportedException: Vertex textures are not supported`). The per-instance fallback currently works but does one draw per instance (no batching).

The Dx12InstancingRepro proved a small standalone `.fx` with only the grouped technique compiles fine — all params present. The bug is triggered by the **combination** of all 8 techniques + their full uniform union in one `.fx` file.

## Approach

Split the skinned-instanced grouped-uniform shaders into their **own .fx files** — `ForwardPbrGrouped.fx` (forward) and `DepthShadowGrouped.fx` (shadow). Each contains only `viewProj`, `matModel`, `bonePaletteGroup[128]`, `groupBoneCount`, and the skinned-instanced PBR/depth pixel shader. This isolates the grouped-uniform params from the large `$Globals` of the main effects so the DX12 reflection parser registers them.

On DX11/Vulkan/OpenGL the existing `ForwardPbr.fx` / `DepthShadow.fx` (VTF) continue to be used unchanged. On DX12 the grouped effects are loaded instead, and the forward-pass `perInstanceFallback` gate switches from per-instance draws to batched grouped-uniform draws.

## Step 1 — Create `ForwardPbrGrouped.fx`

New file: `Mibo/src/Mibo.MonoGame/Shaders/ForwardPbrGrouped.fx`

Contents: the vertex types + `VS_SkinnedInstancedGrouped` / `VS_SkinnedInstancedGroupedColor` (copied from `ForwardPbr.fx:475-520`) plus the **full** PBR pixel shader (full copy of `shadePBR` from `ForwardPbr.fx:528-650` — ambient + directional + point lights + spot lights + shadow atlas sampling, all light arrays included). Two techniques: `SkinnedInstancedGrouped` and `SkinnedInstancedGroupedColor`. Gate the whole file with `#if !OPENGL` so the OpenGL build compiles an empty effect (mgfxc accepts zero-technique effects on DX11/DX12/Vulkan).

Uniforms — the full set from `ForwardPbr.fx`'s `$Globals`:
- `viewProj`, `matModel`, `normalMatrix`, `cameraPos`
- `albedoColor`, `roughness`, `metallic`, `emissionColor`, `opacity`, `tiling`, `useNormalMap`
- `ambientColor`, `ambientIntensity`
- `dirLightDir`, `dirLightColor`, `dirLightIntensity`, `dirLightCastsShadows`
- `pointLightCount`, `pointLightPos[8]`, `pointLightColor[8]`, `pointLightIntensity[8]`, `pointLightRadius[8]`, `pointLightFalloff[8]`, `pointLightShadowIdx[8]`
- `spotLightCount`, `spotLightPos[4]`, `spotLightDir[4]`, `spotLightColor[4]`, `spotLightIntensity[4]`, `spotLightRadius[4]`, `spotLightInnerCutoff[4]`, `spotLightOuterCutoff[4]`, `spotLightShadowIdx[4]`
- `shadowViewProjs[16]`, `shadowUVOffsets[16]`, `shadowTexelSize`, `shadowBiases[16]`, `shadowAtlas`
- `bonePaletteGroup[128]`, `groupBoneCount`
- `texture0`–`texture4` (material maps)

This is the same `$Globals` as `ForwardPbr.fx` minus `paletteTex`/`paletteTexSize` (VTF, not needed) minus `boneMatrices` (per-instance Skinned path, not needed) — giving the DX12 reflection parser only the grouped-uniform params + the standard lighting/shadow uniforms, without the 8-technique union that triggers the param drop.

## Step 2 — Create `DepthShadowGrouped.fx`

New file: `Mibo/src/Mibo.MonoGame/Shaders/DepthShadowGrouped.fx`

Contents: `VS_SkinnedInstancedGrouped` (copied from `DepthShadow.fx:166-184`) plus `PS_Main` (depth-write). One technique: `DepthSkinnedInstancedGrouped`. Uniforms: `viewProj`, `matModel`, `bonePaletteGroup[128]`, `groupBoneCount` only. No texture, no shadow atlas, no bone palette uniform array. Very small `$Globals`.

## Step 3 — Update `script.fsx`

Add `ForwardPbrGrouped.fx` and `DepthShadowGrouped.fx` to the `ShaderList` so all four backend profiles get compiled.

## Step 4 — Update `Mibo.MonoGame.fsproj`

Add `EmbeddedResource` entries for the 8 new `.mgfx` files (4 profiles × 2 shaders):
```
ForwardPbrGrouped.dx.mgfx / .ogl.mgfx / .dx12.mgfx / .vk.mgfx
DepthShadowGrouped.dx.mgfx / .ogl.mgfx / .dx12.mgfx / .vk.mgfx
```

## Step 5 — Load the grouped effects on DX12

In `PbrShading.fs`, add to `PbrResources`:
```fsharp
member val GroupedEffect: Effect voption = ValueNone with get, set
member val GroupedParams: PbrEffectParams voption = ValueNone with get, set
```

Add `ensureGroupedEffect` alongside `ensureEffect` — loads `"ForwardPbrGrouped"` only on DX12 (on other backends it stays `ValueNone`). Builds `PbrUniforms.build` against it.

In `drawAnimatedModelInstanced` (PbrShading.fs:1129), change the DX12 gate from the per-instance fallback to the grouped path:
- `perInstanceFallback` becomes `isOpenGLBackend()` only (DX12 no longer takes it)
- The `else` branch (chunked batched draw, lines 1157-1407) already has an `if isDirectX12Backend() then grouped` sub-branch (lines 1209-1215) — **already wired**, just currently unreachable because the outer gate sends DX12 to per-instance
- In that DX12 sub-branch, switch from `res.Effect` (the main PBR effect) to `res.GroupedEffect` (the isolated grouped effect) and `res.GroupedParams` instead of `res.Params`
- Technique selection simplifies: `SkinnedInstancedGrouped` or `SkinnedInstancedGroupedColor` (no VTF option on DX12)
- Remove the VTF `paletteTex` branch for DX12 — `paletteTex` is always null, always uses the constant array

## Step 6 — Load the grouped shadow effect on DX12

In `ShadowPass.fs`, add to `ShadowResources`:
```fsharp
member val GroupedEffect: Effect voption = ValueNone with get, set
member val GroupedParams: ShadowEffectParams voption = ValueNone with get, set
```

Load `"DepthShadowGrouped"` on DX12 alongside the main `DepthShadow` effect. In `renderSkinnedInstancedSpan` (ShadowPass.fs:888), when `isDX12` is true, use the grouped shadow effect + its params instead of the main shadow effect. The grouped depth technique `DepthSkinnedInstancedGrouped` will have `bonePaletteGroup` + `groupBoneCount` present.

## Step 7 — Upload full lighting uniforms to the grouped effect

The grouped PBR effect mirrors the full `ForwardPbr.fx` uniform set, so `uploadLights` (ambient + directional + point array + spot array + shadow indices) and `bindTextures` work unchanged through the existing null-safe setters. `uploadMaterial` works unchanged (same material maps). Shadow atlas uniforms are uploaded the same way as the main effect — the grouped pixel shader samples `shadowAtlas` for shadows. The only params absent are `paletteTex`/`paletteTexSize` (VTF, not used) and `boneMatrices` (per-instance Skinned, not used) — their setters will null-skip.

## Step 8 — Recompile, verify, test

1. Run `dotnet fsi script.fsx` — verify all backends compile, no errors
2. Byte-scan `ForwardPbrGrouped.dx12.mgfx` — confirm `bonePaletteGroup`, `groupBoneCount` are PRESENT
3. Byte-scan `DepthShadowGrouped.dx12.mgfx` — confirm same
4. Build `Mibo.MonoGame` — verify it compiles
5. Run AnimatedInstancing MonoGame DX12 sample — verify instances render (not blank, not per-instance fallback)
6. Run on DX11, Vulkan — verify no regression (VTF path unchanged, grouped effects never loaded)

## Files touched

| File | Change |
|---|---|
| `Shaders/ForwardPbrGrouped.fx` | **New** — isolated grouped PBR shader |
| `Shaders/DepthShadowGrouped.fx` | **New** — isolated grouped depth shader |
| `Shaders/script.fsx` | Add 2 entries to ShaderList |
| `Mibo.MonoGame.fsproj` | 8 new EmbeddedResource lines |
| `Pipelines/PbrShading.fs` | Add grouped effect loading; change DX12 gate from per-instance to grouped; use grouped effect in DX12 chunked path |
| `Pipelines/ShadowPass.fs` | Add grouped shadow effect loading; use it in DX12 renderSkinnedInstancedSpan |

## Known limitations

- Full PBR lighting + shadow sampling means a large `$Globals` cbuffer. If the DX12 reflection parser still drops `bonePaletteGroup` from this file, the split was not the right fix — the parser may choke on the array regardless of effect size. The byte-scan in Step 8 will tell us immediately.
- OpenGL unchanged — still per-instance fallback (no VTF in `vs_3_0`).
- DX11/Vulkan unchanged — VTF path, never loads grouped effects.