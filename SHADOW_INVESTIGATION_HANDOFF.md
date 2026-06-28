# Shadow Investigation — Full Attempts Chronology & Plan

This doc lists EVERY attempt made to fix the shadow, in order, with: the hypothesis, the measurement that tested it, and the result (worked / didn't / refuted). Then the current verified state of the code, and the plan for what's left.

Branch `feat/monogame3d`, submodule `Mibo` at `a7926db` (detached). **Nothing committed** — all working-tree.

---

## The two distinct problems (do not confuse them)

- **Problem A — VISIBILITY:** no shadows rendered at all (giant black rectangle covering everything, or everything shadowed). **SOLVED.** See attempt #8.
- **Problem B — STABILITY/PLACEMENT:** now that shadows render, the whole shadow atlas jumped when the player moved, rotated with the sun, and was offset from casters. **SOLVED.** See attempts #11, #15, #16.

Every attempt below is tagged **[A]** or **[B]** so you can see which problem it targeted.

---

## Attempts, in chronological order

### [A] #1 — Hypothesis: shadow atlas empty because casters not collected (instanced world skipped)

- **Hypothesis:** `ShadowPass.run` only collected `DrawPrimitive` and `DrawAnimatedModel`; the world renders via `DrawInstanced`, so nothing wrote to the atlas → empty → "shadow everywhere" (reasoned from the shader comparison logic).
- **Measurement:** CPU `GetData` readback of slot 0 reported `min=max=mean=1.000` (the clear color = empty). Plus `instancedCasters=0` confirmed `DrawInstanced` wasn't collected.
- **Result:** Hypothesis CONFIRMED as a real gap. Fix (instanced caster collection — see "Current code state" #2) made the atlas start writing depth (`min≈0.79`).
- **Did it fix the _visible_ shadow?** No on its own — rectangle persisted. But it was a necessary precondition.

### [A] #2 — Hypothesis: forward shader reads 0.0 because `EffectPass.Apply()` clobbers `gd.Textures[5]`

- **Hypothesis:** the atlas was bound only via `gd.Textures[5]`; MonoGame's `Apply()` rebinds sampler textures from the effect's own EffectParameters (per the existing `bindTextures` comment for texture0-4), so slot 5 reset to null each draw → shader sampled 0.0.
- **Measurement:** none direct — reasoning from the `PbrUniforms` comment.
- **Result:** Added `ShadowAtlasTex` EffectParameter resolved as `"texture5"`, set it each shadow pass. **Did NOT fix it.** (Later proven: `"texture5"` is the wrong name — see #8.)
- **Verdict:** Correct instinct (Apply does rebind samplers), but the param-name bug meant this was a no-op the whole time. Kept in tree (harmless once renamed).

### [A] #3 — Hypothesis: bias too small → floor self-shadows (caster == receiver surface)

- **Hypothesis:** the instanced floor is both caster and receiver; the tiny `DirectionalBias=0.0007` couldn't separate them → universal self-shadow.
- **Measurement:** `d`-visualization (shader returned the stored depth as grayscale) showed the whole frustum interior read **uniformly black (d≈0)** while CPU readback said `mean≈0.84`.
- **Result:** Added receiver-side bias (`shadowBiases` uniform + `recvZ = ndc.z - bias`), raised `DirectionalBias` to `0.01`. **Did NOT fix it** — rectangle unchanged. User: "no acne just dark rect."
- **Verdict:** REFUTED as the cause. (The receiver-side bias is still in the tree as a legitimate improvement, but it was NOT the visibility bug.) The `d≈0` vs CPU-`0.84` contradiction was the real clue — it meant the shader wasn't sampling the atlas at all, not that depth was wrong.

### [A] #4 — Hypothesis: rasterizer cull mode wrong (back faces writing depth)

- **Reasoning:** shadow pass uses `CullClockwiseFace`; model imported with `FlipWindingOrder`. Back-face depth would make receivers fail the test.
- **Result:** Worked out by hand: back-face depth makes receivers read `ndc.z < d` → **lit**, the opposite of the symptom. **Eliminated without a test.**

### [A] #5 — Hypothesis: depth-range remap asymmetry (OpenGL [-1,1] vs DX11 [0,1])

- **Reasoning:** `DepthShadow.fx` writes `d = clip.z/w * 0.5 + 0.5`; forward reads `ndc.z * 0.5 + 0.5`. On DX11 clip.z/w is already [0,1], so the remap might double-compress.
- **Measurement:** receiver-`ndc.z` visualization showed the floor at **~0.8 (correct, light gray)**. So the receiver depth was fine; the asymmetry theory predicted it would be wrong.
- **Result:** **REFUTED.** The receiver depth is correct; both sides remap consistently.

### [A] #6 — Hypothesis: `shadowUVOffsets` never reached the shader (Vector4[] marshaling)

- **Hypothesis:** MonoGame's `EffectParameter.SetValue(Vector4[])` doesn't marshal HLSL `float4[]` → offsets stayed default (scale≈1) → receivers sampled the full atlas instead of slot 0's sub-region → read empty texels → shadow.
- **Measurement:** atlas-UV visualization (shader returned `atlasUV.xy × 4` as R,G) showed a **green→orange→red gradient** — i.e. `atlasUV.x` exceeded slot 0's `[0,0.25]`, sampling outside the slot. Looked like confirmation.
- **Result:** Switched to per-element upload (`p.Elements.[i].SetValue`). **Did NOT fix it.** The rectangle persisted.
- **Verdict:** PARTIALLY WRONG. The UV-viz "confirmation" was misleading: it was reading `shadowUVOffsets[0]` (which WAS being uploaded fine) but the _depth_ it sampled was 0 — because the atlas sampler wasn't bound (#8), not because the UV offset was wrong. The per-element upload is still in the tree (it's correct and avoids the `IndexOutOfRangeException` from bulk `SetValue(float[])` — see #7), but it was NOT the visibility fix.

### [A] #7 — Crash: `SetValue(float[])` throws `IndexOutOfRangeException`

- **What happened:** attempt #6's flat-array upload (`setFloatArray` on a `float[active*4]`) crashed because MonoGame requires the array length to EXACTLY match the compiled HLSL array size.
- **Fix:** per-element `setVec4Element`/`setFloatElement` via `p.Elements.[i]`, with a `i < p.Elements.Count` guard. Scratch sized to `MaxCasters`. **Crash gone.** Still in tree.

### [A] #8 — THE VISIBILITY FIX: atlas sampler param name is `"shadowAtlas"`, not `"texture5"`

- **Hypothesis:** the sampler bind resolved to null because the param name was wrong.
- **Measurement (decisive):** dumped every `EffectParameter` from the compiled `ForwardPbr` effect to a file. Output included:
  ```
  name=shadowAtlas semantic= type=Texture2D elements=0
  ```
  There is **no `texture5` parameter**. The sampler declared `sampler2D shadowAtlas : register(s5)` is exposed under its HLSL name `shadowAtlas`.
- **Result:** Changed `PbrUniforms.buildShadow` from `param e "texture5"` → `param e "shadowAtlas"`. **User confirmed: "I can finally see shadows."**
- **Verdict:** THIS was the visibility root cause. Every earlier [A] theory (#2-#6) was chasing symptoms of this one no-op bind. The lesson — dump ground truth first — is in the doc.

### [B] #9 — Hypothesis: instability is just config tuning (bias/snap)

- **Hypothesis:** the jump/rotate/slide was the same class of bug as the visibility issue; tuning `DirectionalBias`/`GridSnapSize` would smooth it.
- **Measurement:** user ran with `DirectionalBias=0.01`, `SlopeScaleBias=1.5`. User: "looks exactly the same as before."
- **Result:** **REFUTED.** Tuning does nothing for the instability. It's a different problem.

### [B] #10 — Hypothesis: instability is a MonoGame matrix-convention difference

- **Hypothesis:** `buildDirectionalViewProj` is a faithful port of Raylib's `createDirectionalShadowCamera`; the instability must come from a backend-specific matrix difference (VP built outside `BeginMode3D` in MonoGame vs inside it in Raylib — `AGENTS.md` warns about this).
- **Measurement:** VP-dump probe added to `buildDirectionalViewProj`; logs raw origin, snapped origin, lightPos, view, proj, final VP, and camera target each frame.
- **Result:** Logs show the VP changes **smoothly** with sun movement and **does not jump** with player movement once origin Y is locked. The apparent "rotation" is the light direction changing, which is geometrically required for a directional light. The remaining "jagged" feel was from `GridSnapSize=2.0`; setting it to `0.0` removes the stepping.
- **Verdict:** PARTIALLY REFUTED for the jump/slide part. The remaining visual issue is not instability of the VP itself, but that the rendered shadows look wrong (giant/misaligned).

### [B] #11 — Hypothesis: depth-space mismatch in the shadow comparison (DX11 vs OpenGL clip z range)

- **Hypothesis:** `ForwardPbr.fx` remaps `ndc.z` to `[0,1]` with `ndc = ndc * 0.5 + 0.5`, but `DepthShadow.fx` writes raw `clip.z/clip.w`. On WindowsDX, `clip.z/clip.w` is already `[0,1]`, so the receiver depth gets remapped to `[0.25,0.75]` while the stored depth stays in `[0,1]` → systematic mismatch → self-shadow / over-shadow.
- **Measurement:** Removed the `ndc.z` remap from `ForwardPbr.fx` and matched `DepthShadow.fx` to write/compare raw clip-space z. Feet-probe delta went from large positive to `~0.000`, confirming the two passes now agree on depth.
- **Result:** World went from "mostly shadowed" to "fully lit", then with further tuning back to "shadows visible but giant/misaligned." The depth-space mismatch was real and needed fixing, but it is not the whole story.
- **Verdict:** REAL BUG, FIXED. Remaining shadow artifacts are not caused by depth-space mismatch.

### [B] #12 — Hypothesis: shadow pass cull mode causes front/side faces of cubes to self-shadow the top faces

- **Hypothesis:** with a low sun, the cube side faces facing the light are written to the shadow map; the ground top surfaces read those side-face depths and fail the comparison → giant self-shadow.
- **Measurement:** Switched shadow-pass culling from `CullClockwiseFace` to `CullCounterClockwiseFace` (records back faces instead of front faces).
- **Result:** No visible change. Shadows still appear as a giant misaligned dark area.
- **Verdict:** REFUTED as the cause of the giant shadow.

### [B] #13 — Hypothesis: instanced shadow draw path is broken

- **Hypothesis:** the terrain is drawn with `DrawInstanced`; if the two-stream `DepthInstanced` technique fails, only the player/skinned casters write to the atlas → small dark blob → giant blurred shadow from PCF sampling empty atlas regions.
- **Measurement:** Replaced the hardware-instanced shadow path with a one-instance-at-a-time fallback using the regular `Depth` technique.
- **Result:** No visible change. The rest of the shadows still behave the same.
- **Verdict:** REFUTED. `DepthInstanced` and the two-stream vertex binding are not the problem.

### [A/B] #14 — Hypothesis: floor cubes should not cast shadows on themselves

- **Hypothesis:** the "giant shadow" is caused by cube terrain self-shadowing; stacked blocks should cast on the floor, but floor cubes should not be casters.
- **Measurement:** Proposed splitting ground blocks from stacked/platform blocks in `View.fs` and drawing ground with `DisableShadows`.
- **Result:** User rejected this as a stopgap; wants the library/shaders to handle cube self-shadowing correctly rather than excluding models.
- **Verdict:** NOT PURSUED as a fix.

### [B] #15 — Hypothesis: shadow sampler wrap mode causes shadow tiling

- **Hypothesis:** `ForwardPbr.fx` declared `sampler2D shadowAtlas : register(s5);` with no `AddressU/V`, so the default wrap mode repeated the small caster region across the ground, creating a grid of dark spots.
- **Measurement:** Added explicit `AddressU = Clamp; AddressV = Clamp;` to the shadow sampler.
- **Result:** Grid of dark spots disappeared. Shadows became localized but were offset horizontally from their casters.
- **Verdict:** REAL BUG, FIXED. The localized-but-offset result pointed to a remaining projection/UV flip issue.

### [B] #16 — Hypothesis: shadow UV has a vertical flip due to DirectX viewport vs texture v axis

- **Hypothesis:** DirectX viewports map clip.y=1 to the top of the render target, while texture v increases downward; the shader's `(ndc.y * 0.5 + 0.5)` maps clip.y=1 to the bottom of the atlas slot, so every shadow is sampled at the wrong vertical texel, appearing as a horizontal displacement.
- **Measurement:** Changed atlas UV to `float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5)`.
- **Result:** User confirmed shadows are finally correctly aligned with casters and color output is restored. The vertical flip was the final UV correction.
- **Verdict:** REAL BUG, FIXED.

---

## Current verified state of the code (what's actually in the tree)

All in `Mibo/src/Mibo.MonoGame/` unless noted.

1. **Atlas sampler bind — `shadowAtlas` (THE visibility fix).** `PbrUniforms.fs:buildShadow` resolves `ShadowAtlasTex = param e "shadowAtlas"`. `ShadowPass.fs` sets `p.Shadow.ShadowAtlasTex.SetValue(res.Atlas.Fbo)` when non-null, plus `gd.Textures[5]`/`SamplerStates[5]` as a safety net. **Verified working by user.**

2. **Instanced caster collection.** `DepthShadow.fx` has `VS_Instanced` + `DepthInstanced` technique. `ShadowPass.fs` collects `DrawInstanced` into `InstancedDraws` and renders them two-stream (mesh + `VertexInstanceWorld`). **Verified: `instancedCasters` went 0 → 56-75; atlas depth went 1.0 → ~0.8.**

3. **Receiver-side bias.** `ForwardPbr.fx`: `float shadowBiases[16]`; `computeShadowAt` does `recvZ = ndc.z - bias; (recvZ > d) ? 0.0 : 1.0`. Uploaded per-element via `setFloatElement`. Sample config uses default `DirectionalBias=0.0005`.

4. **Per-element array uploads.** `PbrUniforms.fs`: `setVec4Element`/`setFloatElement` using `p.Elements.[i]`. `ShadowPass.fs` uses them for `shadowUVOffsets` and `shadowBiases`; `shadowViewProjs` (Matrix[]) stays on bulk `setMatrixArray`. Scratch sized to `MaxCasters=16`, cleared each frame. **Avoids the #7 crash.**

5. **Depth-space fix.** Both `ForwardPbr.fx` and `DepthShadow.fx` now compare raw `clip.z/clip.w` (no `[0,1]` remap). Feet-probe delta ~0.000, confirming the passes agree on depth.

6. **Shadow origin Y locked to 0.** `ShadowPass.fs` no longer follows the camera target's Y; prevents the shadow frustum from sliding vertically when the player jumps.

7. **Grid snap in sample.** `MonoThreeD/Program.fs` sets `GridSnapSize=32.0f` for chunk-aligned stability.

8. **Sample config in use** (`MonoThreeD/Program.fs`): `Resolution=8192`, `DirectionalLightSize=50` (default), `MaxCasters=16` (default), `OriginStrategy=CameraTarget` (default), `GridSnapSize=32.0`, `DirectionalLightDistance=100` (default).

9. **Shadow placement verified.** With the vertical light debug override in `DayNight.fs`, the platform's L-shaped shadow lands directly beneath its caster. After restoring normal PBR output, color returns and shadows remain aligned.

## Ground-truth facts established by measurement

- Compiled `ForwardPbr` effect params (from the dump): `shadowViewProjs`/`shadowUVOffsets`/`shadowBiases` are flat `float[]`, **16 elements each**. Samplers present: `texture0`, `texture2`, `texture4`, `shadowAtlas` (texture1/texture3 stripped — roughness/metallic not sampled). No `texture5`.
- Atlas slot-0 CPU readback (instanced casters writing): `min≈0.76-0.80, max≈0.84-1.0, mean≈0.82`.
- Receiver `ndc.z` at the floor: **~0.81-0.82** (correct, verified via receiver-depth viz).
- Feet-probe (player feet projected through dir VP vs sampled atlas depth): `delta` small and **negative** (`-0.002` to `-0.036`), i.e. receiver depth ≤ stored depth → comparison math is sound at that point.
- Day/night directional intensity never hits 0 (sun fades at dawn/dusk, moon maxes 0.3 at night) — so the darkness was NOT a lighting-curve issue.

---

## What is RULED OUT (do not re-try these)

- Empty atlas / casters-not-collected → fixed (#1), was real but not the visibility symptom.
- `Apply()` clobbering `gd.Textures[5]` → real mechanism but the bind was null anyway (#2); now bound correctly.
- Bias too small / self-shadow acne → refuted (#3) **before the sampler bind was fixed**; with the sampler bound, receiver-side bias plus point-clamp sampler fixes self-shadow artifacts.
- Rasterizer cull mode → eliminated by reasoning (#4); wrong symptom direction. Back-face culling for the shadow pass is retained as a robustness choice.
- Depth-range remap asymmetry → **real bug, now fixed** (#11). Receiver and stored depths now match.
- `shadowUVOffsets` not uploaded (Vector4[] marshaling) → refuted as the visibility cause (#6); the offset WAS uploading, the sampler was just unbound.
- Bias/snap tuning for the jump/slide instability → addressed by origin Y lock + clip-space texel snapping (#10, final implementation).
- `DepthInstanced` / two-stream instanced shadow binding broken → refuted (#13). The fallback one-instance-at-a-time path is still in the tree for safety but should be replaced with the `DepthInstanced` technique in cleanup.
- Front/back face cull mode causing the giant shadow → refuted (#12).
- Shadow tiling/repeating dark spots → **real bug, now fixed** (#15): sampler wrap mode was not clamped.
- Shadow offset beside casters → **real bug, now fixed** (#16): atlas UV needed a vertical flip to match DirectX viewport conventions.

## Final root cause summary

The visible shadow problems were caused by **three independent bugs** in the MonoGame backend:

1. **Sampler bind name mismatch** (#8): `ForwardPbr.fx` exposed the atlas sampler as `shadowAtlas`, but `PbrUniforms.fs` looked for `texture5`. The bind silently no-ops, so the forward shader sampled 0.0 (shadowed) everywhere.
2. **Depth-space mismatch** (#11): `ForwardPbr.fx` remapped `ndc.z` to `[0,1]`, but `DepthShadow.fx` wrote raw `clip.z/clip.w` (already `[0,1]` on DirectX). Receiver and stored depths disagreed.
3. **Atlas UV orientation mismatch** (#16): the shadow atlas is rendered through a DirectX viewport where clip.y=1 is the top of the render target, but texture v increases downward. The lookup needed `v = -ndc.y * 0.5 + 0.5` to align with the viewport.

Secondary polish issues fixed:
- Sampler wrap mode was not clamped, causing the small caster region to tile across the ground (#15).
- Receiver-side bias was added and uploaded to prevent floor self-shadowing now that the instanced world is a caster.
- The shadow frustum origin Y is locked so player jumping does not slide shadows vertically.
- Clip-space / world-space snapping keeps shadow-map pixels stable as the camera or sun moves.
- Instanced geometry is collected into the shadow atlas.

## What is the LEADING UNTESTED hypothesis

None — the shadow rendering issue is resolved. Remaining work is cleanup of temporary debug code (see below).

## Plan — CLEANUP COMPLETED

1. ✅ Made shadow origin Y configurable via `ShadowAtlasConfig.DirectionalOriginY` (default `0.0f`). The per-frame override already existed via the `shadowOrigin: Vector3 voption` argument to `buildDirectionalViewProj`; `DirectionalOriginY` is used when no override is supplied.
2. ✅ Replaced the one-instance-at-a-time fallback in the shadow pass with real hardware instancing via the `DepthInstanced` technique and two-stream vertex bind (`VertexInstanceWorld` on stream 1).
3. ✅ Removed the `shadow_vp_probe.log` VP probe from `ShadowPass.fs`.
4. ✅ Restored the day/night cycle in `MonoThreeD/DayNight.fs` (removed vertical debug override).
5. ✅ Cleaned sample investigation diagnostics: removed `DebugModel`, shadow atlas thumbnail, depth readback, feet-probe, animation gizmo fields; `DiagnosticsView.fs` now shows only basic FPS/position/time/grounded/particles.
6. ✅ Disabled the renderer's `ShowDebugOverlay` in `MonoThreeD/Program.fs`.
7. ⏳ Run `dotnet fantomas .` on both repositories and rebuild shaders before staging.
8. ⏳ Update `Mibo/CHANGELOG.md` with the MonoGame 3D shadow fixes.

## Remaining sample-only items to review

- `MinimapView.fs` is no longer wired into the overlay; it remains in the project and can be re-enabled if desired.
- `BoneProbe/` diagnostic project exists in the working tree but is not part of `MonoThreeD.fsproj`.
- `SHADOW_INVESTIGATION_HANDOFF.md` itself is untracked; decide whether to keep as docs or delete before commit.

---

## Commands

- Build: `dotnet build MonoThreeD/MonoThreeD.fsproj -c Debug`
- Recompile a shader: `mgfxc <name>.fx <name>.dx.mgfx /Profile:DirectX_11` AND `/Profile:OpenGL` (both `.mgfx` are embedded; rebuild both).
- Format before commit (AGENTS.md): `dotnet fantomas .`

## Note on the sample tree

`MonoThreeD/Types.fs`, `Systems.fs`, `View.fs`, `DiagnosticsView.fs` contain debug instrumentation (anim/shadow overlay, atlas readback, bone gizmos) added during this investigation. It is unrelated to the shadow fixes (which are all in the submodule) and can be deleted before committing. `BoneProbe/` is a diagnostic project. The legitimate sample-side change is only the bias config in `Program.fs`.
