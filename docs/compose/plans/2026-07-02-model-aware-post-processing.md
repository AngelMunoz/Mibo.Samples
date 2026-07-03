# Model-Aware Post-Processing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use compose:subagent (recommended) or compose:execute to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make post-processing model-aware on both backends (raylib + MonoGame) in both 2D and 3D, via a view-emitted command that the framework drains after the scene — no pipeline generics, no built-in shaders.

**Architecture:** A new `PostProcess of action: (PostProcessContext -> unit)` command (sibling of `DrawImmediate`/`beginEffect`). The view emits it per frame (model-derived params closed over in the view); the pipeline/renderer owns the offscreen scene RT + ping-pong RT pool, enters the destination target, and invokes the action with `{ Source; Depth?; Width; Height; Time; Context; Device? }`. The user's action resolves its own shader lazily and draws a fullscreen quad of `Source`. Depth-based effects opt in at construction via a linear-depth R32F pre-pass, identical on both backends.

**Tech Stack:** F# · raylib-cs (`Mibo.Raylib`) · MonoGame DesktopGL/WindowsDX (`Mibo.MonoGame`) · Expecto tests · 2MGFX for `.mgfx` compilation.

## Repo / PR ordering

Framework changes live in the `Mibo/` **submodule**; sample validation in `FPSSample/`. Per `AGENTS.md`, open the **framework PR first** — the sample PR can only merge once it lands. Phases 1–4 are the framework; Phase 5 is the sample and depends on them.

---

## Global Constraints

Every task inherits these (from `AGENTS.md` + the design spec):

- **Never use `Option.get` / `ValueOption.get`.** Pattern-match or use `Option.defaultValue` / `ValueOption.defaultValue` etc.
- **Format before staging:** run `dotnet fantomas .` in the repo root of whatever you changed (Mibo submodule, then samples root).
- **raylib scalar uniforms use `fixed` + `NativePtr.toVoidPtr`** (the `DisableRuntimeMarshalling` void* bug — see `Mibo/AGENTS.md`). `SetShaderValueMatrix` is the exception (takes the matrix directly).
- **No built-in shaders in the framework.** The framework ships zero grayscale/fog/vignette effects — only the mechanism. Samples author their own GLSL/`.mgfx`.
- **Depth is opt-in at construction** (a resource-budget flag, like `shadowAtlasConfig`), never per-frame game state. When on, the framework renders a camera-POV linear-depth R32F pass on **both** backends so the sample's depth shader is portable.
- **Color RTs are sampleable; depth-stencil is not** (MonoGame). Depth always comes from the R32F pre-pass, never from `DepthFormat.Depth24`.
- **MonoGame effects are precompiled `.mgfx`** (ogl + dx variants) loaded via `ShaderLoader.loadEffect`; there is no runtime shader compilation.

---

## File Structure

**`Mibo.Raylib` (submodule) — Phase 1 (3D) + Phase 2 (2D):**
- `src/Mibo.Raylib/Graphics3D/PostProcessContext3D.fs` — **create** — the 3D post-process context record + the drain loop helper.
- `src/Mibo.Raylib/Graphics3D/Command3D.fs` — **modify** — add `PostProcess` case + `Command3D.postProcess` factory.
- `src/Mibo.Raylib/Graphics3D/Draw3D.fs` — **modify** — add `Draw3D.postProcess` pipe helper.
- `src/Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs` — **modify** — drain `PostProcess` commands at the existing `applyPostProcess` site (≈ L2555); add construction-time depth opt-in + camera-POV linear-depth pass.
- `src/Mibo.Raylib/Graphics3D/Pipelines/PostProcess3D.fs` — **modify** — remove `PostProcessConfig3D`/`PostProcessPass3D` (replaced by the command).
- `src/Mibo.Raylib/Graphics2D/Renderer2D.fs` — **modify** — remove `PostProcessPass`/`PostProcess2D.apply`/`Renderer2DConfig.PostProcess`; drain `PostProcess` commands in `Draw` (≈ L546).
- `src/Mibo.Raylib/Graphics2D/Command2D.fs` — **modify** — add `PostProcess` case + factory.
- `src/Mibo.Raylib/Graphics2D/Draw.fs` — **modify** — add `Draw2D.postProcess` helper.
- `src/Mibo.Raylib/Graphics2D/PostProcessContext2D.fs` — **create** — the 2D context record + drain loop.

**`Mibo.MonoGame` (submodule) — Phase 3 (3D) + Phase 4 (2D):**
- `src/Mibo.MonoGame/Graphics3D/PostProcessContext3D.fs` — **create**.
- `src/Mibo.MonoGame/Graphics3D/Command3D.fs` — **modify** — add `PostProcess` case + factory.
- `src/Mibo.MonoGame/Graphics3D/Draw3D.fs` — **modify** — add helper.
- `src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs` — **modify** — `Execute` (L692) renders forward pass to an offscreen RT when post-process commands exist; implement the ping-pong fullscreen-quad drain (currently gated out at L929); add depth opt-in.
- `src/Mibo.MonoGame/Graphics3D/FullScreenQuad.fs` — **create** — lazy vertex buffer for the fullscreen quad (`DrawUserIndexedPrimitives`).
- `src/Mibo.MonoGame/Graphics3D/Pipelines/PostProcess3D.fs` — **modify** — remove eager config.
- `src/Mibo.MonoGame/Graphics2D/*` — mirror Phase 2 shapes.

**`FPSSample` — Phase 5:**
- `FPSSample/Shared/HudLayout.fs` — keep the `intensity` math, drop the rectangle-overlay color path.
- `FPSSample/Raylib/View.fs` — emit `Draw3D.postProcess` while `isHitFlash model`.
- `FPSSample/Raylib/Shaders/` — **create** — `grayscale.vs.glsl` + `grayscale.fs.glsl`.
- `FPSSample/MonoShared/` — resolve the `.mgfx` via `IAssets`; emit the MonoGame post-process command.

---

## Phase 1 — raylib 3D (canonical pattern)

Establishes the command + context + drain that every other phase mirrors. The raylib 3D pipeline **already** renders the scene to `sceneRT` and calls `applyPostProcess` (`ForwardPbrPipeline.fs:2555`), so this phase is the smallest net change.

### Task 1.1: Define `PostProcessContext3D` (raylib)

**Covers:** [S4]
**Files:**
- Create: `Mibo/src/Mibo.Raylib/Graphics3D/PostProcessContext3D.fs`
- Modify: `Mibo/src/Mibo.Raylib/Mibo.Raylib.fsproj` (add the file to compile order, near `Command3D.fs`)

**Produces:** `PostProcessContext3D` record consumed by Task 1.3 and 1.4.

- [ ] **Step 1: Create the context record**

```fsharp
namespace Mibo.Elmish.Graphics3D

open Raylib_cs
open Mibo.Elmish

/// <summary>
/// Context handed to a <c>Command3D.PostProcess</c> action each frame. The pipeline
/// has already rendered the scene to <c>Source</c> and entered the destination render
/// target (a pooled ping-pong RT, or the back-buffer for the last pass). The action
/// resolves its own shader, sets model-derived params, and draws a fullscreen quad of
/// <c>Source</c>.
/// </summary>
[<Struct>]
type PostProcessContext3D = {
  /// <summary>Current ping-pong source (the scene RT on the first pass).</summary>
  Source: RenderTexture2D

  /// <summary>Camera-POV linear depth (R32F). ValueNone unless depth was opted in at construction.</summary>
  Depth: Texture2D voption

  Width: int
  Height: int

  /// <summary>Accumulated frame time in seconds, for animated effects.</summary>
  Time: float32

  /// <summary>Game services — e.g. <c>tryGetService&lt;IAssets&gt;</c> to resolve a shader lazily.</summary>
  Context: GameContext
}
```

- [ ] **Step 2: Add the file to the fsproj**

Add `<Compile Include="Graphics3D/PostProcessContext3D.fs" />` immediately before `Graphics3D/Command3D.fs` in `Mibo.Raylib.fsproj` (the context must be defined before the command that references it).

- [ ] **Step 3: Build**

Run: `dotnet build Mibo/src/Mibo.Raylib`
Expected: PASS (record compiles; nothing references it yet).

- [ ] **Step 4: Commit**

```bash
git -C Mibo add src/Mibo.Raylib/Graphics3D/PostProcessContext3D.fs src/Mibo.Raylib/Mibo.Raylib.fsproj
git -C Mibo commit -m "feat(raylib): add PostProcessContext3D record"
```

---

### Task 1.2: Add the `PostProcess` command + factory (raylib 3D)

**Covers:** [S4], [S8]
**Files:**
- Modify: `Mibo/src/Mibo.Raylib/Graphics3D/Command3D.fs` (add case at L66 after `DrawImmediate`; add factory in the `Command3D` module at L158)
- Modify: `Mibo/src/Mibo.Raylib/Graphics3D/Draw3D.fs` (add pipe helper near the other helpers)
- Test: `Mibo/src/Mibo.Raylib.Tests/Graphics3DTests.fs`

**Consumes:** `PostProcessContext3D` (Task 1.1).
**Produces:** `Command3D.PostProcess`, `Command3D.postProcess`, `Draw3D.postProcess`.

- [ ] **Step 1: Write the failing test**

Add to `Graphics3DTests.fs` (Expecto style, matching existing `testCase` blocks):

```fsharp
testCase "post-process command round-trips through the buffer"
(fun _ ->
  let buffer = RenderBuffer3D()
  let called = ref false
  let action (_: PostProcessContext3D) = called := true

  buffer |> Draw3D.postProcess action |> Draw3D.drop |> ignore

  Expect.equal buffer.Count 1 "one command added"
  match buffer[0] with
  | Command3D.PostProcess a ->
    a Unchecked.defaultof<_>
    Expect.isTrue !called "action is invokable"
  | _ -> failwith "expected PostProcess command")
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Mibo/src/Mibo.Raylib.Tests --filter "post-process command round-trips"`
Expected: FAIL — `Draw3D.postProcess` / `Command3D.PostProcess` not defined.

- [ ] **Step 3: Add the command case + factory**

In `Command3D.fs`, add after `DrawImmediate` (L67):

```fsharp
  | PostProcess of action: (PostProcessContext3D -> unit)
```

In the `Command3D` module (after `drawImmediate`, L158):

```fsharp
  let inline postProcess (action: PostProcessContext3D -> unit) =
    Command3D.PostProcess(action)
```

In `Draw3D.fs` (after the `drawImmediate`-style helpers):

```fsharp
  /// <summary>
  /// Enqueues a model-aware post-process pass. The action runs once, after the whole
  /// scene renders to an offscreen target; it receives the scene texture (+ optional
  /// depth) and must draw a fullscreen quad. Emit conditionally from the view based on
  /// game state. Chain multiple passes — they ping-pong in buffer order.
  /// </summary>
  let inline postProcess
    (action: PostProcessContext3D -> unit)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.postProcess action)
    buffer
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Mibo/src/Mibo.Raylib.Tests --filter "post-process command round-trips"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
dotnet fantomas Mibo/src/Mibo.Raylib/Graphics3D/Command3D.fs Mibo/src/Mibo.Raylib/Graphics3D/Draw3D.fs Mibo/src/Mibo.Raylib.Tests/Graphics3DTests.fs
git -C Mibo add src/Mibo.Raylib/Graphics3D/Command3D.fs src/Mibo.Raylib/Graphics3D/Draw3D.fs src/Mibo.Raylib.Tests/Graphics3DTests.fs
git -C Mibo commit -m "feat(raylib): add Command3D.PostProcess + Draw3D.postProcess"
```

---

### Task 1.3: Drain `PostProcess` commands in the raylib 3D pipeline

**Covers:** [S4], [S6], [S8]
**Files:**
- Modify: `Mibo/src/Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs` — replace the `applyPostProcess` body (≈ L1763) to drain commands; call it with the buffer's collected actions at L2555.
- Modify: `Mibo/src/Mibo.Raylib/Graphics3D/Pipelines/PostProcess3D.fs` — remove `PostProcessConfig3D`/`PostProcessPass3D`/`PostProcessConfig3D.none`.
- Modify: callers that pass `postProcess=` to `ForwardPbrPipeline` — there are none in-repo (FPS `Program.fs:55` omits it); update the `ForwardPbrPipeline` constructor signature to drop the `?postProcess` arg.
- Test: `Mibo/src/Mibo.Raylib.Tests/Graphics3DTests.fs` (the existing test at L184 references `PostProcessPasses` — update it).

**Consumes:** `Command3D.PostProcess`, `PostProcessContext3D`.
**Produces:** a model-aware raylib 3D post-process path (color only; depth in Task 1.4).

- [ ] **Step 1: Collect PostProcess actions during Execute**

In `ForwardPbrPipeline.fs`, the forward loop already pattern-matches commands (≈ L2325–L2470). The `PostProcess` case must be *collected*, not drawn inline. Add a `ResizeArray<PostProcessContext3D -> unit>` (hoist a `let ppActions = ResizeArray<_>()` near the other per-frame mutable state at ≈ L1756), and handle the case in the dispatch loop:

```fsharp
          | Command3D.PostProcess action -> ppActions.Add action
```

- [ ] **Step 2: Rewrite `applyPostProcess` to drain collected actions**

Replace the `ppPasses`-based `applyPostProcess` (L1763) so it takes the action list and invokes each with a `PostProcessContext3D`, ping-ponging through the RT pool. The last pass draws to the back-buffer (no `BeginTextureMode`):

```fsharp
  let applyPostProcess
    (ctx: GameContext)
    (sceneTarget: RenderTexture2D)
    (rtPool: IRenderTargetPool3D)
    (actions: ResizeArray<PostProcessContext3D -> unit>)
    (depthTex: Texture2D voption)
    =
    if actions.Count = 0 then () else
    let mutable src = sceneTarget
    let w = ctx.WindowWidth
    let h = ctx.WindowHeight

    for i = 0 to actions.Count - 1 do
      let isLast = i = actions.Count - 1
      let dst: RenderTexture2D voption =
        if isLast then ValueNone else ValueSome(rtPool.Acquire(w, h))

      match dst with
      | ValueSome target ->
        Raylib.BeginTextureMode target
        Raylib.ClearBackground Color.Black
      | ValueNone -> ()

      let ppCtx: PostProcessContext3D = {
        Source = src
        Depth = depthTex
        Width = w
        Height = h
        Time = frameTime
        Context = ctx
      }
      actions[i] ppCtx

      match dst with
      | ValueSome target ->
        Raylib.EndTextureMode()
        src <- target
      | ValueNone -> ()
```

(`frameTime` is already in scope in `Execute`; thread it in, or capture it as a field. Prefer passing it as a param to keep the function pure.)

- [ ] **Step 3: Wire the call site + remove eager config**

At L2555 replace `applyPostProcess gameCtx sceneRT rtPool` with `applyPostProcess gameCtx sceneRT rtPool ppActions ValueNone` (`ValueNone` for depth until Task 1.4). Drop the `?postProcess` parameter from the `ForwardPbrPipeline` constructor (L1701) and the `ppConfig`/`ppPasses` bindings (L1708, L1758). Delete `PostProcess3D.fs`'s `PostProcessConfig3D`/`PostProcessPass3D` types and `PostProcessConfig3D.none`.

Update `Graphics3DTests.fs:184` (`PostProcessPasses = ValueNone`) — that field no longer exists; remove the line (the test constructs a pipeline without post-process, which is now the only mode).

- [ ] **Step 4: Manual visual verification**

There is no sample using raylib 3D post-process yet (that's Phase 5). Verify by build + a temporary scratch: temporarily emit a no-op `Draw3D.postProcess (fun _ -> ())` in `FPSSample/Raylib/View.fs` after `endCamera` and confirm the sample still renders identically (the drain is a no-op pass-through when the action draws nothing — acceptable for a smoke test). Then revert the scratch before commit.

Run: `dotnet run --project FPSSample/Raylib`
Expected: scene renders normally with the temporary no-op command present.

- [ ] **Step 5: Build + test**

Run: `dotnet build Mibo/src/Mibo.Raylib && dotnet test Mibo/src/Mibo.Raylib.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
dotnet fantomas Mibo
git -C Mibo add -A
git -C Mibo commit -m "feat(raylib 3D): drain PostProcess commands from the buffer"
```

---

### Task 1.4: Depth opt-in — camera-POV linear-depth pass (raylib 3D)

**Covers:** [S4-D]
**Files:**
- Modify: `Mibo/src/Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs` — add `?postProcessDepth: bool` constructor arg; when set, render a linear-depth R32F pass and pass it as the `depthTex` arg added in Task 1.3.

**Consumes:** the `depthTex` parameter from Task 1.3.
**Produces:** `PostProcessContext3D.Depth` populated on opt-in.

- [ ] **Step 1: Add the opt-in flag**

Add `?postProcessDepth: bool` to the `ForwardPbrPipeline` constructor (L1700). Bind `let wantDepth = defaultArg postProcessDepth false`.

- [ ] **Step 2: Render linear depth when opted in**

Acquire an R32F target from the RT pool (extend `IRenderPool3D.Acquire` is out of scope — instead allocate a dedicated `RenderTexture2D` lazily, sized to the window, format `PixelFormat.UncompressedR32G32B32A32` or a single-channel equivalent raylib exposes; reuse across frames). Render the scene's camera-POV depth into it using the existing depth-only shader technique already present for shadows (`Shaders.loadDepthShadowShader` / `ForwardPbrPipeline.fs:2150` — reuse `depthShadowShader`, which writes depth to a single channel). Run this pass with `state.View`/`state.Projection` (the camera) before the forward pass writes color, or as a separate depth-only pass over the same geometry.

> **Note for the implementer:** the shadow depth shader writes light-POV depth; for camera-POV fog you need view/linear depth. Author a tiny `grayscale`-style depth fragment that outputs linearized view-space Z (the sample owns the *consumption* shader; the framework owns this *producer* pass). Verify the channel written matches what the sample's fog shader samples.

- [ ] **Step 3: Pass depth into the drain**

At the L2555 call site, pass `ValueSome depthTarget.Texture` instead of `ValueNone` when `wantDepth`.

- [ ] **Step 4: Manual verification (deferred to Phase 5)**

Depth consumption is validated by a sample fog effect. No sample ships one in this plan, so this task is verified by build + a unit assertion that the flag plumbing does not break the no-depth path:

Add a test: constructing `ForwardPbrPipeline(postProcessDepth = true)` builds without throwing, and `PostProcessContext3D.Depth` is `ValueSome` when the flag is on (assert via a captured action in the buffer, like Task 1.2's test).

- [ ] **Step 5: Build + test + commit**

```bash
dotnet build Mibo/src/Mibo.Raylib && dotnet test Mibo/src/Mibo.Raylib.Tests
dotnet fantomas Mibo
git -C Mibo add -A
git -C Mibo commit -m "feat(raylib 3D): opt-in camera-POV linear-depth pass for post-process"
```

---

_(Phase 1 establishes the canonical shape. Phases 2–4 mirror it; Phase 5 consumes it.)_

---

## Phase 2 — raylib 2D (no pipeline; flat renderer)

`Renderer2D<'Model>` (`Renderer2D.fs:514`) is already generic over the model and already owns `sceneRT` + `rtPool`. It currently branches on eager `config.PostProcess` (`Renderer2D.fs:546`). This phase swaps that for command-driven drain. **No depth for 2D in this plan** (2D depth effects are rare; add later if needed — the context's `Depth` is `ValueNone` here).

### Task 2.1: `PostProcessContext2D` + `Command2D.PostProcess` (raylib)

**Covers:** [S4], [S5]
**Files:**
- Create: `Mibo/src/Mibo.Raylib/Graphics2D/PostProcessContext2D.fs`
- Modify: `Mibo/src/Mibo.Raylib/Graphics2D/Command2D.fs` (add case + factory), `Draw.fs` (add `Draw2D.postProcess`), `Mibo.Raylib.fsproj`
- Test: `Mibo/src/Mibo.Raylib.Tests/Graphics2DTests.fs`

**Produces:** `PostProcessContext2D`, `Command2D.PostProcess`, `Draw2D.postProcess`.

- [ ] **Step 1: Define the context**

```fsharp
namespace Mibo.Elmish.Graphics2D

open Raylib_cs
open Mibo.Elmish

[<Struct>]
type PostProcessContext2D = {
  Source: RenderTexture2D
  Width: int
  Height: int
  Time: float32
  Context: GameContext
}
```

- [ ] **Step 2: Failing test (mirror Task 1.2)**

```fsharp
testCase "2d post-process command round-trips"
(fun _ ->
  let buffer = RenderBuffer2D()
  buffer |> Draw2D.postProcess (fun (_: PostProcessContext2D) -> ()) |> ignore
  Expect.equal buffer.Count 1 "added"
  match buffer[0] with
  | Command2D.PostProcess _ -> () // pass
  | _ -> failwith "expected PostProcess")
```

- [ ] **Step 3: Add case + factories** — `| PostProcess of action: (PostProcessContext2D -> unit)` in `Command2D.fs`; `Command2D.postProcess` + `Draw2D.postProcess` (same shape as Task 1.2 Step 3).

- [ ] **Step 4: Run test → PASS. Commit:**

```bash
dotnet fantomas Mibo && git -C Mibo add -A && git -C Mibo commit -m "feat(raylib 2d): add Command2D.PostProcess"
```

---

### Task 2.2: Command-driven drain in `Renderer2D.Draw` (raylib)

**Covers:** [S5]
**Files:**
- Modify: `Mibo/src/Mibo.Raylib/Graphics2D/Renderer2D.fs` — remove `PostProcessPass` (L11), `PostProcess2D.apply` (L27), `Renderer2DConfig.PostProcess` (L97); rewrite the `Draw` branch (L546).

- [ ] **Step 1: Rewrite the Draw branch**

Peek the buffer for `PostProcess` commands; render to `sceneRT` only when present (preserves the no-RT hot path); drain with ping-pong, reusing the logic from the deleted `PostProcess2D.apply`:

```fsharp
let ppActions = ResizeArray<PostProcessContext2D -> unit>()
for i = 0 to buffer.Count - 1 do
  match buffer[i] with
  | Command2D.PostProcess a -> ppActions.Add a
  | _ -> ()

if ppActions.Count = 0 then
  // unchanged hot path
  match config.ClearColor with ValueSome c -> Raylib.ClearBackground c | ValueNone -> ()
  CommandHandlers.execute(&state, buffer)
else
  let sceneRT = rtPool.Acquire(ctx.WindowWidth, ctx.WindowHeight)
  Raylib.BeginTextureMode(sceneRT)
  match config.ClearColor with ValueSome c -> Raylib.ClearBackground c | ValueNone -> ()
  CommandHandlers.execute(&state, buffer)
  Raylib.EndTextureMode()
  let mutable src = sceneRT
  let mutable time = 0.0f // thread real frame time in via the renderer if available
  for i = 0 to ppActions.Count - 1 do
    let isLast = i = ppActions.Count - 1
    let dst = if isLast then ValueNone else ValueSome(rtPool.Acquire(ctx.WindowWidth, ctx.WindowHeight))
    match dst with ValueSome t -> Raylib.BeginTextureMode t; Raylib.ClearBackground Color.Black | ValueNone -> ()
    ppActions[i] { Source = src; Width = ctx.WindowWidth; Height = ctx.WindowHeight; Time = time; Context = ctx }
    match dst with ValueSome t -> Raylib.EndTextureMode(); src <- t | ValueNone -> ()
  rtPool.ReleaseAll()
```

Update `Renderer2DConfig.defaults`/`noClear` to drop the `PostProcess` field. Update `Graphics2DTests.fs:1250` (`cfg.PostProcess.IsSome`) — that assertion is removed (no eager post-process field anymore).

- [ ] **Step 2: Build + test**

Run: `dotnet build Mibo/src/Mibo.Raylib && dotnet test Mibo/src/Mibo.Raylib.Tests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
dotnet fantomas Mibo && git -C Mibo add -A && git -C Mibo commit -m "feat(raylib 2d): drain PostProcess commands, remove eager config"
```

---

## Phase 3 — MonoGame 3D (net-new: offscreen RT + ping-pong + fullscreen quad)

The hardest phase. MonoGame 3D has **no** post-process today (`ForwardPipeline.fs:929` gate). `Execute(gameCtx, gameTime, buffer, _rtPool)` (L692) renders the forward pass **directly to the back-buffer**. This phase adds: (a) render forward to an offscreen `sceneRT` when post-process commands exist, (b) a fullscreen quad primitive, (c) the ping-pong drain, (d) depth opt-in.

### Task 3.1: `PostProcessContext3D` + `Command3D.PostProcess` (MonoGame)

**Covers:** [S4]
**Files:**
- Create: `Mibo/src/Mibo.MonoGame/Graphics3D/PostProcessContext3D.fs`
- Modify: `Mibo/src/Mibo.MonoGame/Graphics3D/Command3D.fs`, `Draw3D.fs`, `Mibo.MonoGame.fsproj`
- Test: `Mibo/src/Mibo.MonoGame.Tests/` (add if a test project exists; otherwise add to an existing one — check first)

- [ ] **Step 1: Define the MonoGame context** (note `Device` + `Depth` as `RenderTarget2D`):

```fsharp
namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

[<Struct>]
type PostProcessContext3D = {
  Source: RenderTarget2D
  Depth: RenderTarget2D voption
  Width: int
  Height: int
  Time: float32
  Device: GraphicsDevice
  Quad: FullScreenQuad
  Context: GameContext
}
```

- [ ] **Step 2–4:** Failing test (mirror Task 1.2 with MonoGame types) → add `| PostProcess of action: (PostProcessContext3D -> unit)` + factories → PASS → commit. If no MonoGame test project exists, **create** `Mibo.MonoGame.Tests` mirroring `Mibo.Raylib.Tests` structure (Expecto) — this is scaffolding the framework currently lacks; fold it into this task.

```bash
dotnet fantomas Mibo && git -C Mibo add -A && git -C Mibo commit -m "feat(monogame 3d): add Command3D.PostProcess"
```

---

### Task 3.2: Fullscreen-quad primitive

**Covers:** [S6]
**Files:**
- Create: `Mibo/src/Mibo.MonoGame/Graphics3D/FullScreenQuad.fs`

**Produces:** `FullScreenQuad.Draw(effect)` — binds a 4-vertex quad (position + UV) and calls `DrawIndexedPrimitives`.

- [ ] **Step 1: Implement the quad**

```fsharp
namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>A lazy fullscreen quad for post-process blits. Built against the device on first use.</summary>
type FullScreenQuad(gd: GraphicsDevice) =
  let verts = [|
    VertexPositionTexture(Vector3(-1f, -1f, 0f), Vector2(0f, 1f))
    VertexPositionTexture(Vector3(-1f,  1f, 0f), Vector2(0f, 0f))
    VertexPositionTexture(Vector3( 1f,  1f, 0f), Vector2(1f, 0f))
    VertexPositionTexture(Vector3( 1f, -1f, 0f), Vector2(1f, 1f)) |]
  let indices = [| 0us; 1us; 2us; 0us; 2us; 3us |]
  let vb = new VertexBuffer(gd, typeof<VertexPositionTexture>, 4, BufferUsage.WriteOnly)
  let ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly)
  do vb.SetData(verts); ib.SetData(indices)

  /// <summary>Draws the quad with the given effect applied. Caller sets render target + sampler.</summary>
  member _.Draw(effect: Effect) =
    gd.SetVertexBuffer(vb); gd.Indices <- ib
    for p in effect.CurrentTechnique.Passes do
      p.Apply()
      gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, 0, 2)

  interface System.IDisposable with
    member _.Dispose() = vb.Dispose(); ib.Dispose()
```

> **UV note:** MonoGame stores `RenderTarget2D` data upside-down vs raylib (`Renderer2D.fs:77` notes this). The UVs above (V=1 at bottom) are the starting guess; **flip V if the post-processed image is upside-down** during manual verification (Phase 5 / Task 3.4).

- [ ] **Step 2: Build. Commit:**

```bash
dotnet fantomas Mibo && git -C Mibo add -A && git -C Mibo commit -m "feat(monogame): add FullScreenQuad primitive"
```

---

### Task 3.3: Offscreen forward pass + post-process drain in `ForwardPipeline.Execute`

**Covers:** [S6], [S9]
**Files:**
- Modify: `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs` — `Execute` (L692): collect `PostProcess` actions in the pre-scan loop (L728); when present, render the forward pass to an offscreen `sceneRT` (set `gd.SetRenderTarget(sceneRT)` before the forward loop, restore to `null` after); replace the L929 gate with a real ping-pong drain using `FullScreenQuad`.
- Modify: `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/PostProcess3D.fs` — remove eager `PostProcessConfig3D`/`PostProcessPass3D`.
- Drop the `?postProcess` arg from `ForwardPipeline` (L966) and `ForwardPipelineBase` (L186).

- [ ] **Step 1: Collect actions + conditional offscreen RT**

In `Execute`, hoist `let ppActions = ResizeArray<PostProcessContext3D -> unit>()` and in the pre-scan loop add `| Command3D.PostProcess a -> ppActions.Add a`. Before the forward pass, if `ppActions.Count > 0`, acquire `sceneRT` from `_rtPool` (drop the underscore — it's now used) and `gd.SetRenderTarget(sceneRT)`; clear it. After the forward pass, `gd.SetRenderTarget(null)`.

- [ ] **Step 2: Replace the L929 gate with the drain**

```fsharp
if ppActions.Count > 0 then
  let mutable src = sceneRT
  let mutable i = 0
  for action in ppActions do
    let isLast = i = ppActions.Count - 1
    let dst = if isLast then null else _rtPool.Acquire(src.Width, src.Height)
    if not isLast then gd.SetRenderTarget(dst)
    gd.Clear(ClearOptions.Target, Microsoft.Xna.Framework.Color.Black, 0f, 0)
    // The user's action binds src onto sampler slot 0, applies its Effect, and calls Quad.Draw(effect).
    action { Source = src; Depth = ValueNone; Width = src.Width; Height = src.Height
            Time = frameTime; Device = gd; Quad = fullScreenQuad; Context = gameCtx }
    if not isLast then src <- dst
    i <- i + 1
```

> **API contract for the MonoGame user action:** unlike raylib (where the action calls `DrawTexturePro`), on MonoGame the action must set `gd.Textures[0] <- src`, apply its `Effect`, and call `ppCtx.Quad.Draw(effect)`. The `Quad` field on the context makes each action self-contained.

- [ ] **Step 3: Build + test**

Run: `dotnet build Mibo/src/Mibo.MonoGame && dotnet test Mibo/src/Mibo.MonoGame.Tests`
Expected: PASS.

- [ ] **Step 4: Manual verification (deferred to Phase 5)** — no MonoGame post-process sample exists yet; the sample conversion (Phase 5) is the visual gate.

- [ ] **Step 5: Commit**

```bash
dotnet fantomas Mibo && git -C Mibo add -A && git -C Mibo commit -m "feat(monogame 3d): offscreen forward pass + PostProcess drain"
```

---

### Task 3.4: Depth opt-in (MonoGame 3D)

**Covers:** [S4-D]
**Files:** Modify `ForwardPipeline.fs` — add `?postProcessDepth: bool`; allocate a `SurfaceFormat.Single` (R32F) `RenderTarget2D`; render camera-POV linear depth using the existing `DepthShadow.fx` technique (`ShadowAtlas.fs:276` is the precedent — depth to `.r`); pass as `Depth = ValueSome depthRT`.

- [ ] **Step 1–3:** Mirror Task 1.4 — add flag, render linear depth to R32F, populate `Depth`. Verify the no-depth path stays `ValueNone`. The depth *producer* uses the same compiled depth shader the shadow pass already ships; author/compile an ogl+dx `.mgfx` if the shadow one is light-POV only.

```bash
dotnet fantomas Mibo && git -C Mibo add -A && git -C Mibo commit -m "feat(monogame 3d): opt-in linear-depth pass for post-process"
```

---

## Phase 4 — MonoGame 2D

Mirror Phase 2 against `Mibo.MonoGame/Graphics2D` (`Renderer2D.fs`, `Command2D.fs`, `Draw.fs`, `RenderTargetPool.fs`). `Renderer2D.Draw` already sets render targets (`Renderer2D.fs:1700`). Same shape: add `PostProcessContext2D` (`Source: RenderTarget2D`; no depth for 2D), the command + factory, and swap the eager `config.PostProcess` branch for command-driven drain using `FullScreenQuad`. **No depth.**

- [ ] **Task 4.1:** `PostProcessContext2D` + `Command2D.PostProcess` + `Draw2D.postProcess` (failing test → impl → pass → commit). **Covers:** [S4], [S5].
- [ ] **Task 4.2:** Drain in `Renderer2D.Draw`; remove eager `Renderer2DConfig.PostProcess` + `PostProcess2D.apply`; reuse `FullScreenQuad`. Build + test + commit. **Covers:** [S5].

_(Each MonoGame 2D task follows the exact TDD shape of its raylib 2D counterpart in Phase 2, swapping `RenderTexture2D`→`RenderTarget2D`, `BeginTextureMode`→`gd.SetRenderTarget`, and `DrawTexturePro`→`FullScreenQuad.Draw`.)_

---

## Phase 5 — FPSSample hit-flash validation (sample repo; depends on Phases 1 & 3)

Convert the damage overlay from a 2D rectangle (`HudLayout.fs:86` → `HudView.fs:83`) into a real desaturation post-process pass whose `intensity` comes from `model.Effect.HitEffectTimer`.

### Task 5.1: Author the grayscale shader (raylib GLSL)

**Covers:** [S7]
**Files:** Create `FPSSample/Raylib/Shaders/grayscale.vs.glsl` + `grayscale.fs.glsl`.

- [ ] **Step 1: Vertex shader** — passthrough position + UV (standard raylib fullscreen-quad VS; copy from `Mibo/src/Mibo.Raylib/Graphics3D/Pipelines/Shaders.fs` post-process VS if present).

- [ ] **Step 2: Fragment shader**

```glsl
#version 330
in vec2 fragTexCoord;
uniform sampler2D texture0;   // raylib binds the source here
uniform float intensity;      // 0 = full color, 1 = full grayscale
out vec4 finalColor;

void main() {
  vec4 c = texture(texture0, fragTexCoord);
  float gray = dot(c.rgb, vec3(0.299, 0.587, 0.114));
  finalColor = vec4(mix(c.rgb, vec3(gray), intensity), c.a);
}
```

- [ ] **Step 3: Commit**

```bash
git add FPSSample/Raylib/Shaders
git commit -m "feat(fps): add grayscale post-process shader (raylib)"
```

---

### Task 5.2: Emit the post-process command (raylib 3D view)

**Covers:** [S7]
**Files:** Modify `FPSSample/Raylib/View.fs`.

- [ ] **Step 1: Resolve the shader lazily + emit the command**

Near the top of `View.fs`, add a memoized shader ref (resolved once via `Raylib.LoadShader` against `AppContext.BaseDirectory`-relative paths — see `Program.fs:42` for the base-path convention). At the end of `view` (after `endCamera`, L309), emit conditionally:

```fsharp
if HudLayout.isHitFlash model then
  let intensity = model.Effect.HitEffectTimer / Constants.HitEffectDuration
  let setIntensity (pp: PostProcessContext3D) =
    let loc = Raylib.GetShaderLocation(grayscaleShader, "intensity")
    use p = fixed &intensity
    Raylib.SetShaderValue(grayscaleShader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Float)
    Raylib.BeginShaderMode grayscaleShader
    let src = Rectangle(0f, 0f, float32 pp.Width, float32 -pp.Height)  // negative height: raylib FBO flip
    let dst = Rectangle(0f, 0f, float32 pp.Width, float32 pp.Height)
    Raylib.DrawTexturePro(pp.Source.Texture, src, dst, Vector2.Zero, 0f, Raylib_cs.Color.White)
    Raylib.EndShaderMode()
  buffer |> Draw3D.postProcess setIntensity |> Draw3D.drop
```

> The `fixed + NativePtr.toVoidPtr` for the scalar uniform is **mandatory** (`Mibo/AGENTS.md` void* bug).

- [ ] **Step 2: Remove the rectangle overlay** in `FPSSample/Raylib/HudView.fs:83` (the `isHitFlash` rectangle draw). Keep `HudLayout.isHitFlash` (still useful) but the `hitFlashColor` rectangle path is now dead — remove it.

- [ ] **Step 3: Manual visual verification**

Run: `dotnet run --project FPSSample/Raylib`
Expected: on taking damage, the **whole 3D scene desaturates** and recovers as `HitEffectTimer` decays — not a gray rectangle drawn over the HUD. Confirm the HUD (health/ammo) still draws normally (it's a separate `Renderer2D`).

- [ ] **Step 4: Build + run the shared test suite**

Run: `dotnet test FPSSample/Shared.Tests`
Expected: PASS (the shared logic is unchanged; the overlay was view-only).

- [ ] **Step 5: Commit**

```bash
dotnet fantomas . && git add -A && git commit -m "feat(fps/raylib): drive hit-flash as a post-process desaturation pass"
```

---

### Task 5.3: MonoGame parity (hit-flash)

**Covers:** [S7]
**Files:**
- Create `FPSSample/MonoShared/Shaders/grayscale.ogl.mgfx` + `grayscale.dx.mgfx` (compile the Phase 5.1 GLSL/HLSL via 2MGFX; see `Mibo/src/Mibo.MonoGame/Graphics2D/ShaderLoader.fs` for the embedded-resource + ogl/dx convention).
- Modify `FPSSample/MonoShared/HudView.fs:87` (remove the rectangle) and the MonoShared view to emit the MonoGame `PostProcess` command via `IAssets`, using `ppCtx.Quad.Draw(effect)` + `gd.Textures[0] <- src`.

- [ ] **Step 1:** Author HLSL grayscale matching the GLSL, compile both `.mgfx` variants, embed as resources (mirror the LitSprite resource embedding in `Mibo.MonoGame.fsproj`).
- [ ] **Step 2:** Emit the MonoGame post-process command (intensity from the same `HitEffectTimer` math).
- [ ] **Step 3:** Manual verification on **both** MonoGame clients (`FPSSample/MonoDesktop` DesktopGL + `MonoWindowsDX`): the desaturation matches the raylib result.
- [ ] **Step 4:** Commit.

```bash
dotnet fantomas . && git add -A && git commit -m "feat(fps/mono): hit-flash post-process on MonoGame (ogl+dx)"
```

---

## Self-Review (run before handing off)

- [ ] **Spec coverage** — every spec anchor is in some task's **Covers:**: S2 (goal — header), S3 (command-based — 1.2/2.1/3.1/4.1), S4 (context — 1.1/3.1), S4-D (depth — 1.4/3.4), S5 (2D — 2.2/4.2), S6 (3D drain — 1.3/3.3), S7 (sample — 5.1–5.3), S8 (single callback — 1.2), S9 (irreducible — 3.3), S10 (API surface — all). No orphan `Covers:` IDs.
- [ ] **Type consistency** — `PostProcessContext3D` field names match across definition (1.1/3.1), drain (1.3/3.3), and sample (5.2/5.3). MonoGame context has `Device` + `Quad`; raylib does not. `Depth` is `Texture2D voption` (raylib) vs `RenderTarget2D voption` (MonoGame) — distinct per backend.
- [ ] **Placeholder scan** — Task 1.4 and 3.4 mark depth-producer shader authoring as implementer notes rather than full code, because the *consumer* (sample fog) is out of this plan's scope and the producer channel must match it. Flagged explicitly, not hidden.

## Execution order & dependencies

```
Phase 1 (raylib 3D) ──┐
Phase 2 (raylib 2D) ──┤── framework (Mibo submodule) ── merge first
Phase 3 (mono   3D) ──┤
Phase 4 (mono   2D) ──┘
                      └──► Phase 5 (FPSSample) ── merge after framework lands
```

Phases 1–4 are independent of each other (different backends/dimensions) and can run in parallel; Phase 5 needs 1 & 3. Open the framework PR first, then the sample PR (`AGENTS.md`).
