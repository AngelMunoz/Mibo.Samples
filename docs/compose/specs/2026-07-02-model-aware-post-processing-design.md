# Model-Aware Post-Processing — Design

Status: **Design (not yet implemented)** · Scope: `Mibo.Raylib` + `Mibo.MonoGame`, 2D and 3D · Validated against: `FPSSample` hit-flash

## [S1] Problem

Post-processing cannot react to game state today. Three concrete blockers:

1. **No per-frame model channel into passes.** `PostProcessPass3D.OnSetup: (Shader -> GameContext -> unit)` runs each frame but `GameContext` (`Mibo.Core/Elmish.Rendering.fs:15`) carries only window size + a service dictionary — **no game model**. So a grayscale pass cannot read `EffectModel.HitEffectTimer`, and a fog pass cannot read world state. The FPSSample therefore fakes damage feedback with a 2D rectangle overlay (`HudLayout.fs:86` → `HudView.fs:83`) instead of a real desaturation pass.
2. **Passes are baked eagerly at construction.** `PostProcessConfig3D` is passed once to `ForwardPbrPipeline(...)` (`Program.fs:55`). The carrier (`Shader`/`Effect`) is captured before `GraphicsDevice`/`IAssets` exist, which is why MonoGame resolution is awkward.
3. **MonoGame 3D post-process does not exist.** `ForwardPipelineBase` gates it out explicitly (`ForwardPipeline.fs:929`, *"B9 wires the full post-process chain"*). Passes are silently ignored. MonoGame also cannot compile effects at runtime — only precompiled `.mgfx` resources load (`ShaderLoader.fs:60`).

## [S2] Goal & non-goals

**Goal:** a general, opt-in mechanism that lets a sample drive a post-process pass from the live model on both backends (raylib + MonoGame), in both 2D and 3D, without making any pipeline generic over the model.

**Non-goals (explicitly out of scope):**
- **No built-in effect library.** The framework ships no grayscale/fog/vignette shaders. It provides the *mechanism*; samples author their own GLSL/`.mgfx` and opt in. (A built-in library can be layered later on top of this mechanism.)
- **No generic pipeline.** The pipeline stays `Model`-agnostic.
- **No implementation in this spec** — design only.

## [S3] Core decision: command-driven, modeled on `DrawImmediate` / `beginEffect`

Post-processing becomes a **command the view emits**, not config the pipeline is constructed with. This mirrors two existing opt-in escape hatches:

- `Command3D.DrawImmediate of (SceneContext -> unit)` (`Command3D.fs:67`) — a callback the pipeline runs at a specific lifecycle point, handing the user the **already-computed** scene data.
- `Command3D.BeginEffect`/`EndEffect` (`Command3D.fs:65`) — a command that opts draws into user-shader shading via name-resolved `SceneUpload`, where the framework pushes its computed matrices/lights into the user shader (`ForwardPbrPipeline.fs:2376`).

Post-process is the same idea at a different lifecycle point: **after the whole scene renders to an offscreen target**. The framework owns the offscreen RT + the ping-pong RT pool (what it already calculates); the user owns the shader + model-derived params (what only the view knows).

**Why this avoids heavy pipeline surgery:** the pipeline edit is a *single* post-render hook that drains `PostProcess` commands from the buffer and hands each one the current source texture + RT pool. The pipeline learns nothing about passes, params, or the model. No constructor/Init/Execute threading of post-process state. raylib already has the execution site (`applyPostProcess` at `ForwardPbrPipeline.fs:2555`) and the scene RT; MonoGame gets one new additive hook.

**Why no generics:** the pipeline already consumes model-derived data (lights, camera, draws) every frame through the command buffer without knowing the model type. Post-process is one more command kind — the view is the model→command translator, exactly as it is for `AddPointLight`.

## [S4] The primitive: a post-process command + context

One new command per dimension. The action receives a **backend-specific context** carrying the framework's computed artifacts. Opt-in escape hatches are inherently backend-specific (as `DrawImmediate` and `BeginEffect` already are), so the context type lives in each backend — consistent with the existing pattern.

```
// 3D (raylib)  — sibling of DrawImmediate in Command3D
| PostProcess of action: (PostProcessContext3D -> unit)

// 3D (MonoGame)
| PostProcess of action: (PostProcessContext3D -> unit)

// 2D — same shape in Command2D
| PostProcess of action: (PostProcessContext2D -> unit)
```

**Context contract** — the framework provides; the user latches on:

| Field | raylib | MonoGame | Purpose |
|---|---|---|---|
| Source | `RenderTexture2D` | `RenderTarget2D` | current ping-pong source (scene RT on first pass) |
| Width / Height | `int` | `int` | fullscreen-quad size |
| Time | `float32` | `float32` | accumulated frame time (for animated effects) |
| Context | `GameContext` | `GameContext` | services — e.g. `tryGetService<IAssets>` to resolve an `Effect` lazily |
| Device | — | `GraphicsDevice` | MonoGame-only; needed to apply an `Effect` |
| Depth | `Texture2D voption` | `RenderTarget2D voption` | camera-POV linear depth (R32F). `ValueNone` unless opted in (see S4-D). Sampled for distance effects (fog, SSAO) |

The framework **enters the destination render target before invoking the action** and leaves it afterward (raylib: `BeginTextureMode(dst)` … action … `EndTextureMode()`; MonoGame: `gd.SetRenderTarget(dst)` … action). The destination is an acquired ping-pong RT for all passes except the last, which draws to the back-buffer. The user's action only: **resolve its shader, set model-derived params, draw a fullscreen quad of `Source`.**

**Carrier resolution (MonoGame) is the user's choice inside the action.** Because `GameContext` is in the context, the user resolves lazily — e.g. `ctx |> GameContext.tryGetService<IAssets> |> ValueOption.bind (fun a -> a.Effect "Grayscale")`. The framework defines no `Resolver` abstraction; the resolver pattern from discussion is simply *one way* the user writes their action. This keeps the framework surface minimal (per the no-built-ins decision) while still solving the eager-resolution timing problem (resolution is deferred to first frame, where the device/assets exist).

### [S4-D] Depth source for distance effects (fog, SSAO)

Distance-based post-process needs the scene depth buffer as a second shader input. This exposes a hard backend asymmetry, and the design resolves it for shader portability.

**The facts (verified in code):**
- **raylib:** depth is natively a sampleable texture. The shadow atlas attaches depth as `FramebufferAttachTextureType.Texture2D` (`ShadowAtlas.fs:240`) into `RenderTexture2D.Depth`. Sampling depth in a post-process is straightforward.
- **MonoGame:** the hardware depth buffer is **not** shader-readable. Scene RTs use `DepthFormat.Depth24` (`RenderTargetPool3D.fs:61`) — a depth-stencil surface, not a bindable texture. You cannot sample it. (Color `RenderTarget2D`s sample fine — that's how the whole post-process color chain works. The restriction is depth-only.)
- **Mibo already works around this** for shadows: the MonoGame shadow atlas is `SurfaceFormat.Single` (R32F) with depth written to `.r` by `DepthShadow.fx` (`ShadowAtlas.fs:276`). The linear-depth pre-pass technique is proven in this codebase.

**Design decision — opt-in linear-depth pass, identical on both backends:**
- A game that needs depth-based post-process opts in at **construction** (a resource-budget flag, like the existing `shadowAtlasConfig` / `maxPointLights` constructor args — not per-frame game state).
- When opted in, the pipeline renders a **camera-POV linear-depth pass into an R32F target** on **both** backends during the scene pass, and exposes it as `PostProcessContext.Depth`.
- Color-only games (grayscale, vignette, blur, tone-map) leave it off → **zero depth cost**, unchanged hot path.
- Because both backends produce the same R32F linear-depth texture, **the fog shader is identical cross-backend** — it samples a linearized depth, never relying on platform depth-texture support. (raylib *could* skip the pre-pass and sample its native depth, but that would force per-backend depth-reconstruction math in the shader; unifying on the R32F pre-pass is chosen for portability.)

The pre-pass uses the same depth-writing shader technique already present for shadows; it is a camera-POV pass, so it cannot reuse the light-POV shadow atlas.

## [S5] 2D path — `Renderer2D` (no pipeline)

`Renderer2D<'Model>.Draw` (`Renderer2D.fs:530`) is a flat sequence and is already generic over `'Model`; the view already receives the model. Today it branches on `config.PostProcess` (eager passes). The change is localized entirely inside `Draw`:

```
view ctx model buffer
buffer.Sort()
hasPP = buffer contains any PostProcess command
if hasPP:
    sceneRT = rtPool.Acquire(w, h)
    BeginTextureMode(sceneRT); clear; CommandHandlers.execute(...); EndTextureMode()
    drain PostProcess commands with ping-pong (framework owns RT swap)
else:
    clear; CommandHandlers.execute(...)        // unchanged hot path — no RT cost
rtPool.ReleaseAll()
```

Key points:
- The hot path (no post-process) is **unchanged** — no offscreen RT, no extra blit. The renderer only allocates an RT on frames where the view emitted a `PostProcess` command.
- The view emits the command **conditionally** based on the model (e.g. only while `isHitFlash model`), so the RT cost is paid only during the effect.
- The eager `config.PostProcess` path is removed in favor of the command (it was never model-aware). `PostProcess2D.apply` is replaced by the drain loop.

This is the easiest quadrant — no pipeline object, no generics question, the renderer already owns `sceneRT` + `rtPool`.

## [S6] 3D path — pipeline post-render hook

The pipeline (`ForwardPbrPipeline` / `ForwardPipeline`) renders the scene to an offscreen `sceneRT` and, at the end of `Execute`, currently calls `applyPostProcess gameCtx sceneRT rtPool` (raylib, `ForwardPbrPipeline.fs:2555`). The change:

1. **raylib:** at the existing hook, drain `PostProcess` commands from the buffer (instead of `ppConfig.Passes`). The ping-pong loop and fullscreen blit (`DrawTexturePro`) are reused unchanged — only the *source* of passes switches from baked config to buffer commands. `PostProcessConfig3D` as a constructor argument is removed.
2. **MonoGame:** implement the offscreen scene RT + ping-pong chain + `Effect` apply from scratch (it is gated out today). This is **additive** (a new post-render section in `Execute`) — it does not touch existing camera/light/shadow/forward logic. Once built, it drains the same `PostProcess` commands.

Both backends must render the scene to an offscreen RT when (and only when) at least one `PostProcess` command is present. raylib largely does this already; MonoGame must add it. The drain loop:

```
src = sceneRT
for i, cmd in postProcessCommands:
    isLast = (i = last)
    dst = if isLast then backbuffer else rtPool.Acquire(w, h)
    enterRenderTarget(dst); clear
    cmd.Action { Source = src; Width; Height; Time; Context; Device?; Depth? }
    exitRenderTarget(dst)
    src <- dst
```

If depth is opted in (S4-D), the pipeline renders the camera-POV linear-depth R32F pass **before** this drain loop and passes it as `Depth`. The pre-pass output is the same across all chained passes (depth doesn't ping-pong).

## [S7] Sample validation — FPSSample hit-flash

Convert the damage overlay from a 2D rectangle into a real post-process pass to prove the model-aware path end to end:

- Author a desaturation shader (GLSL pair for raylib; `.mgfx` ogl+dx for MonoGame) exposing a single `intensity` uniform.
- Resolve lazily in the action (raylib `LoadShader` at first use; MonoGame via `IAssets`).
- In the raylib 3D view, emit the post-process command only while `isHitFlash model`, with `intensity` computed from `model.Effect.HitEffectTimer / Constants.HitEffectDuration` (the same math currently in `HudLayout.hitFlashColor`).
- Remove the rectangle overlay (`HudView.fs:83`) on the path that now uses post-process.

This validates: conditional emission, model-derived params, lazy carrier resolution, and (once MonoGame is wired) cross-backend parity.

## [S8] Open design detail — single terminal callback (decided)

A post-process is a **terminal, post-scene** step — nothing scopes around it (unlike `beginEffect`, which wraps draws). Therefore the command is a **single action callback** (`PostProcess of action`), not a `beginPostProcess`/`endPostProcess` pair. Chaining multiple effects = multiple sequential `PostProcess` commands in the buffer, ping-ponged by the framework (S6 loop). A scoped pair would imply draw interleaving that cannot occur at post-scene time.

## [S9] Irreducible work (must exist regardless of command vs config)

- **MonoGame 3D:** offscreen scene RT + ping-pong chain + fullscreen-quad `Effect` apply. Not "modification" of existing logic — net-new implementation behind a single `Execute` hook. Unavoidable: MonoGame post-process does not exist today.
- **Offscreen RT conditionally:** both backends must render to an RT on frames that have post-process commands (raylib: mostly present; MonoGame: new).
- **Depth pre-pass (opt-in):** a camera-POV linear-depth R32F pass on both backends, using the same depth-writing technique already in the shadow shaders. Required only when a game opts into depth-based effects (fog, SSAO).

Everything else — pass state, params, carrier resolution, model awareness — lives in the view/command and touches the pipeline at exactly one drain point.

## [S10] Summary of API surface

- `Command3D.PostProcess of action: (PostProcessContext3D -> unit)` — raylib + MonoGame
- `Command2D.PostProcess of action: (PostProcessContext2D -> unit)` — raylib + MonoGame
- `Draw3D.postProcess` / `Draw2D.postProcess` factory helpers
- `PostProcessContext2D/3D` — backend-specific records (Source, Width, Height, Time, Context, Device?, Depth?)
- Construction-time depth opt-in flag (resource budget, like `shadowAtlasConfig`) — enables the camera-POV linear-depth R32F pass on both backends (S4-D)
- Removed: `PostProcessConfig3D` constructor arg; eager `Renderer2DConfig.PostProcess`; `PostProcess2D.apply` (replaced by framework drain loop)

The view is the sole model→post-process translator; the pipeline/renderer stays model-agnostic and non-generic.
