# DX12 Instancing Repro

Minimal repro of the two techniques Mibo uses for skinned instancing, showing what breaks on the MonoGame DX12 backend.

Run it and read the console output. On DX12 you'll see:

```
paletteTex        = OK
paletteTexSize    = OK
bonePaletteGroup  = OK
groupBoneCount    = OK
VTF CRASHED: Vertex textures are not supported on this device.
```

## Technique 1 — VTF (vertex texture fetch) — CRASHES

Bone palettes go into a texture, the vertex shader samples it. Works on DX11, Vulkan, and raylib. On DX12 the MonoGame backend hard-throws `NotSupportedException` the moment you bind a texture to the vertex stage. It doesn't silently return zeros — it crashes the draw call outright. This is the blocker that forced the fallback.

## Technique 2 — Grouped uniform (`float4x4[]` in `$Globals`) — broken in Mibo, works here

The repro shader has a `float4x4[4]` array and it works — params resolve, draw succeeds. But Mibo's real `ForwardPbr.fx` uses `float4x4[128]` and the params come out **absent** from the compiled DX12 effect:

```
ForwardPbr.dx12.mgfx:
  bonePaletteGroup    ABSENT
  groupBoneCount      ABSENT
  paletteTexSize      ABSENT

ForwardPbr.dx.mgfx (DX11):
  bonePaletteGroup    PRESENT
  groupBoneCount      PRESENT
  paletteTexSize      PRESENT
```

This is a fresh recompile, not a stale blob. The DX12 mgfx reflection parser (`ShaderProfile.DirectX12.cs`) drops the params — likely the `CBufferParam` regex fails to match large `float4x4[N]` arrays or the `int`/`float2` scalars that accompany them. At runtime Mibo's null-safe setters silently no-op, the VS reads a zeroed cbuffer, vertices collapse to the origin, nothing renders.

The small repro passes because it's tiny — 4-bone array, two techniques. Mibo's full shader with `bonePaletteGroup[128]` alongside the VTF path and many shared `$Globals` across techniques triggers the failure. So the grouped path was never going to work on DX12 either, and the per-instance fallback is the only option with the current toolchain.