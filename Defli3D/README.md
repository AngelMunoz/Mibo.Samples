# Defli3D — Adaptive Architecture Trace Assessment (3D)

**Date:** 2026-08-15
**Status:** Analysis only. The tools in `../tools` were parameterized during this
assessment (`--adaptive`, query argv, `--draw`/`--update` markers, `--vsync`).
**Scope:** The 3D port of Defli (adaptive tower defense; same Mibo.Adaptive
simulation, three backends captured). The reference is the 2D post-joinOn
assessment in `../Defli/README.md` (section 6): 60 fps, 0.55 ms/frame busy,
0.18 ms/frame adaptive.

| Trace | Backend | Wall | Cadence | Busy (wall) | Adaptive (wall) |
| --- | --- | --- | --- | --- | --- |
| A `MonoDX12.exe_20260813_125642` | MonoDX12 | 242.6 s | 60 Hz (91 % of frames) | 13.0 % | 0.44 % |
| B `Raylib.exe_20260813_170851` | Raylib | 483.4 s | ~30 Hz (49 % at 33 ms) | 4.4 % | 0.26 % |
| C `MonoDX12.exe_20260813_172612` | MonoDX12 | 557.0 s | 60 Hz (81 % of frames) | 9.3 % | 0.40 % |
| D `MonoDX12.exe_20260815_104949` | MonoDX12 | 104.3 s | 60 Hz (97.3 % of frames) | 14.8 % | 0.40 % |
| E `Raylib.exe_20260815_112342` | Raylib | 754.3 s | ~30 Hz (63.1 % at 33 ms) | 6.6 % | 0.34 % |

Sessions D and E are the late-game follow-up: waves 36+ (the 20–35 range of
A–C), game speed up to 4× per the capture session. They validate the current
commit (presenter views, frame-carried clock, zero-copy model parts). The file
names are the default artifact names of the capture sessions (the
`<process>_<timestamp>` naming of `dotnet-trace collect`). The trace files
are local capture artifacts; they are **not stored in the repository**.
Section 5 captures and reproduces them from a clean checkout.

## 1. Method note

- The traces use the evented format with the `CPU_TIME` pseudo-frame.
  Exclusive-interval tables attribute all wall time to `CPU_TIME`. They are
  not usable. The sample census is the valid measurement (one sample per
  distinct timestamp, each ≈ 1 ms of busy time). Same method as the 2D
  assessment.
- The adaptive library namespace is `Mibo.Adaptive` (the 2D-era name
  `AdaptiveSlop.Core` is gone). The census matches the module prefix before
  the `!`, so the shell (`AdaptiveMonoGameGame`, `AdaptiveHeadless`) is not
  counted as adaptive machinery.
- **DoUpdate/StepCore "opens" are NOT frame counts.** The evented profile is
  reconstructed from 1 kHz samples. A frame marker appears only when a sample
  catches it (~30–50 % of frames). The frame count comes from the vsync
  cadence histogram in `probe-structure.fsx` (inter-sample gaps bucketed by
  16.67 ms multiples; k=1 = one vsync = clean frame).
- **The sim steps once per rendered frame, at the backend's real frame time**
  (the hosts build `ElapsedGameTime` from the backend clock; the game has no
  time-scale control). The session context (waves 36+, up to 4×) shows up as
  heavier per-tick work, not as more steps per second. D holds 60 steps/s at
  60 Hz; E runs ~30 steps/s at its ~30 Hz pacing. Per-frame numbers below use
  nominal frame counts (60 Hz for the MonoGame traces, observed ~30 Hz for
  raylib): D ÷6 257, E ÷22 629.
- The missed-vsync tail (k=2, k=3) is the trace collector's doing. The user
  reports the tracer causes hitches; the games hold their pacing outside
  those (D is the cleanest capture yet: k=1 at 97.3 %).

## 2. Macroscopic — what the game is doing

The game is not CPU-bound. The thread is busy 6.6–14.8 % of wall time. The
frame pace is set by the renderer path, not by the simulation.

| Metric | 2D post-joinOn | A (MonoDX12) | B (Raylib) | C (MonoDX12) | D (MonoDX12) | E (Raylib) |
| --- | --- | --- | --- | --- | --- | --- |
| Busy per frame | 0.55 ms | 2.16 ms | 1.48 ms | 1.55 ms | 2.47 ms | 2.19 ms |
| Update side | ~47 % (pre-joinOn) | 20.5 % | 42.3 % (StepCore) | 26.6 % | 22.2 % | 39.2 % |
| Draw side | 6.5 % | 65.2 % | 41.5 % (Renderer3D) | 58.7 % (DoDraw) | 56.1 % (Renderer3D) | 58.5 % (Renderer3D) |
| zeroCreate samples | 1 219 | 5 429 | 6 660 | 10 898 | 2 528 | 12 721 |
| GCs / STW max | 188 / 0.69 ms | 271 / 1.40 ms | 212 / 0.79 ms | 499 / 1.49 ms | 129 / 1.04 ms | 450 / 0.62 ms |

Draw-side composition (share of busy samples):

- **D:** `WorldView.Render` 42.8 % of busy (MapView 17.9 % incl. the instanced
  volume pass 17.9 %, of which 16.4 % is native `CPU_TIME`; TowersView 10.4 %;
  ProjectilesView 2.8 %; EnemiesView 1.9 %; hoverOverlays 0.7 %), pipeline
  `Execute` 6.4 %, HUD 0.3 %. The MonoGame native draw component is unchanged
  in shape.
- **E:** `WorldView.Render` 40.1 % (TowersView **22.2 %** — up from 13.5 % in
  B, and ~70 % of it is `InstanceGroups.Add`; ProjectilesView 4.8 %;
  EnemiesView 3.7 %; VfxView 0.9 %; rangeRing 0.2 %). The raylib backend still
  has **no per-frame MapView pass** (1 sample). Its CPU draw cost is dominated
  by the tower groups (the maxed late-game build) plus the per-group
  doubling growth: `InstanceGroups.Add` is 92.3 % of the trace's zeroCreate
  (0.52 samples/frame, vs 0.41 in B).
- The update side grew with the session: 22.2 % (D) / 39.2 % (E) of busy —
  the waves-36+ tick does ~2× the per-frame work of the 20–35 sessions
  (0.55 / 0.86 ms per frame, vs 0.37–0.44 in A–C). Section 3 attributes it.

Cadence observations:

- **D holds 60 Hz almost perfectly: k=1 97.3 %, k=2 2.5 %** — the cleanest of
  the five sessions. One gap ≥120 ms total (the collector attach). The
  wave-cadence native stalls of session A did not reproduce (the longest
  native span here is 536 ms, at the session edge).
- E is paced at ~30 Hz like B (k=1 63.1 %, k=2 32.7 %), with a k≥3 tail of
  4.2 % and 111 gaps ≥120 ms — 57 of them in the first 2.5 min (attach) and
  43 in the last 2 min (detach); mid-session ~1.4/min. This is the tracer
  hitch pattern, not game work (the longest native span is 851 ms, at the
  session edges). The user reports the hitches are the tracing tool's doing —
  known finding.

## 3. Microscopic — what the simulation is doing

Per-frame sim cost (all five 3D traces vs the 2D post-joinOn reference):

| Item | A (÷14 553) | B (÷14 470) | C (÷33 419) | D (÷6 257) | E (÷22 629) | 2D post-joinOn |
| --- | --- | --- | --- | --- | --- | --- |
| `Towers.tick` | 0.177 ms | 0.225 ms | 0.167 ms | 0.111 ms | 0.210 ms | 0.159 ms |
| `Projectiles.tick` (homing) | 0.135 ms | 0.129 ms | 0.107 ms | 0.062 ms | 0.087 ms | 0.114 ms |
| `Enemies.tick` | 0.061 ms | 0.074 ms | 0.054 ms | **0.309 ms** | **0.278 ms** | ~0.05 ms |
| `Waves.tick` | — | — | — | 0.015 ms | 0.024 ms | — |
| **Mibo.Adaptive machinery** | **0.074 ms** | **0.088 ms** | **0.066 ms** | **0.067 ms** | **0.112 ms** | **0.18 ms** |
| Frame budget used | 13.0 % | 8.9 % | 9.3 % | 14.8 % | 6.6 % | 3.3 % |

- **Mibo.Adaptive is 0.067–0.112 ms/frame in the late game — still 1.6–2.7×
  cheaper per frame than the 2D post-joinOn (0.18 ms).** D matches A/C almost
  exactly (0.067 vs 0.066–0.074); E grew 27 % vs B (0.112 vs 0.088) — and that
  growth is entity volume, not machinery: the enemy-view join chain
  (FilterMapNode 2.4 % of E's busy, JoinMapNode 2.0 % — reads that scale with
  alive enemies) while the sim's own update side grew ~2×.
- All adaptive samples are in the update side. The draw side reads the
  packed `RenderFrame` and touches zero adaptive frames (0 samples in draw,
  both sessions).
- The adaptive work is the read-time gate: per-key node reads
  (`AdaptiveNode<float>` cooldownA 0.7 % / 1.3 % of busy, `voption<int>`
  targetA 0.3 % / 0.7 %), the `Views` join chain above, `ElementMapNode`
  ScanElements 0.4 % (E), and the Homing `JoinMapNode` — whose `CreateEntry`
  is now ~0 (2 samples in D, ~15 in E; the per-entry version checks in
  `DrainJournal` 0.2–1.2 %).
- The write side is dead: `pushMapDelta`/`OnDeltas`/`CommitJournal`/
  `ChangeableMap.Apply` ≈ 0 samples in both; `diffSubscriptions` is 29 samples
  in D (0.005 ms/frame) and 236 in E (0.010 ms/frame).
- **The new #1 sim cost is `Enemies.tick`: 0.28–0.31 ms/frame — ~5× the
  waves 20–35 sessions — with ~100 % of its samples inside
  `List<int>.AddWithResize` (the tick's lazily-allocated per-tick
  `ResizeArray` buffers for arrivals/expired growing through the doubling
  ladder; 1 923 growth events in D ≈ 0.31/frame).** The wave-36+ tick collects
  far more arrivals/expirations per step than the 20–35 range; every tick
  pays the growth from scratch. Game code, not adaptive (the cmap journal is
  a reused array — `ChangeableMap` internals show ~0 samples).
- Allocation drip: sim-side zeroCreate is flat and linear in entities
  (adaptive `Recompute` arrays ≈ 0.02 samples/frame in D, 0.04 in E — same as
  C's 0.037). The renderer owns the allocations: `InstanceScratch`
  (`InstanceGroups.Append`/`Add`) is 94.7 % (D) / 92.3 % (E) of every trace's
  zeroCreate — the per-group doubling growth during the entity ramp.
- GC (from the nettrace, `tools/gcprobe`): all pauses < 1.1 ms (D max 1.04,
  E max 0.62; D 128/129 under 1 ms, E all under 1 ms). MonoDX12 allocates at
  the highest rate yet (1.24/s, vs 0.90–1.12 in A/C — the heavier late-game
  tick plus the InstanceScratch growth); Raylib rose with the entity load
  (0.60/s, vs 0.44 in B). STW totals: D 61 ms, E 117 ms over their sessions.
  GC cannot be felt: the worst pause is 6 % of one frame budget.

## 4. Verdict

- **Mibo.Adaptive does not drag the game, not even at waves 36+.** It costs
  0.067–0.112 ms/frame (≈0.4–0.5 % of a 16.7 ms budget; 2.7–5.1 % of busy
  samples) — MonoDX12 identical to the 20–35 sessions, Raylib +27 % purely
  from entity-volume reads. The write side is dead, the draw side reads the
  packed frame with zero adaptive frames, and the Recompute allocation rate
  is unchanged per frame. The sim's own plain-code costs grew 5×
  (Enemies.tick) while the adaptive layer grew 1.3× — the graph absorbs the
  late-game load better than the sim's per-tick lists.
- **The new #1 sim cost is `Enemies.tick`'s per-tick buffer growth**
  (0.28–0.31 ms/frame, ~5× the previous range): freshly allocated
  `ResizeArray`s per tick, grown through the doubling ladder on every
  arrival/expiration burst. Reuse cleared buffers (clear-and-keep-capacity)
  or a pooled scratch; this is game code in `Shared`, not the framework, and
  it is the single biggest sim-side win available.
- **The renderer still owns the frame.** Draw is 56.1 % (D) / 58.5 % (E) of
  busy; the sim is 22.2 % / 39.2 %. The `InstanceScratch` growth is the top
  allocation source and the GC driver. The MonoGame draw path carries the
  same native component (`CPU_TIME` under the instanced volume pass and the
  pipeline) that the raylib path does not; Raylib's draw cost is the tower
  groups (TowersView 22.2 % of busy, 2.4× the B session — the maxed
  late-game build).
- **The frame pace is environmental, not architectural.** D holds 60 Hz at
  97.3 % — the cleanest capture yet; E paces at ~30 Hz with the collector
  hitch pattern in the tail (known finding). Nothing in the trace points at
  the sim or the adaptive graph for a missed frame.

Watch items, in order:

1. **`Enemies.tick` per-tick buffer growth** (0.28–0.31 ms/frame at waves
   36+, ~5× the 20–35 range; ~100 % of its samples in `List<int>.AddWithResize`)
   — reuse/pool the per-tick `ResizeArray`s. Game code, not adaptive; the
   biggest sim-side item.
2. **InstanceScratch growth** (94–92 % of allocations, 0.38–0.52 zeroCreate
   samples/frame) — pool or pre-size the per-group arrays; it drives the GC
   rate (MonoDX12 hit 1.24 GCs/s in D).
3. **Raylib TowersView** (22.2 % of busy, up from 13.5 %) — the late-game
   tower count plus the per-group doubling; same InstanceScratch fix, plus
   confirm on real GPU hardware.
4. **Native draw path in MonoGame** (instanced volume 16.4 % native,
   pipeline 6.4 %) — validate on GPU hardware; the 1–2 s wave-cadence stalls
   of session A did not reproduce.
5. **Raylib 30 Hz pacing** — confirm vsync/target-fps behavior on real
   hardware; the CPU is 6.6 % busy, so nothing in the sim explains it.
6. **Homing join per-key reads** — still the largest adaptive line
   (`JoinMapNode` 2.0 % of busy in E), and the one that scales with
   projectile volume; `CreateEntry` itself is now ~0.
7. **HUD strings** — raylib `Path.Combine` (AssetsService.resolvePath) 364
   samples + `PrintFormatToString` 129; MonoDX12
   `PrintFormatToStringThen` 17. View-side micro-optimization, not an
   adaptive issue.

## 5. Reproduction

The traces are not stored in the repository. Capture them, then analyze
them with the tools in `../tools`. All commands below run from the
repository root (`../`).

### 5.1 Capture

Prerequisites:

- .NET SDK (the `dotnet trace` diagnostics command; otherwise install the
  equivalent global tool: `dotnet tool install -g dotnet-trace` and use
  `dotnet-trace` below).
- A built backend, e.g. `dotnet build Defli3D/MonoDX12 -c Release` (or
  `Defli3D/Raylib`).

Attach to a running game (the method used for these traces):

```bash
# list .NET processes and find the game pid
# (dotnet trace ps also works on Windows)
dotnet trace ps

# attach: gc-verbose keeps the GC events the gcprobe needs
# Ctrl+C finalizes the file; it also finalizes when the app exits
dotnet trace collect --format SpeedScope --profile gc-verbose -p <pid>
```

This produces `<Process>_<timestamp>.speedscope.json` in the current
directory (the names in the tables above). The GC probe additionally needs
the `.nettrace`: if the capture above did not keep one, capture again
without `--format` and convert it:

```bash
# nettrace variant (for tools/gcprobe)
dotnet trace collect --profile gc-verbose -p <pid>
dotnet trace convert --format SpeedScope -o <name>.speedscope.json <name>.nettrace
```

Capture from the get-go (trace the app at launch):

```bash
dotnet trace collect --format SpeedScope --profile gc-verbose -- Defli3D/MonoDX12/bin/Release/<tfm>/MonoDX12.exe
```

(`<tfm>` is the project's target framework, see `Defli3D/MonoDX12/MonoDX12.fsproj`.)

Session shape that reproduces the findings: start at wave 1 and play into
the end-game waves (20–35), keep the game fully warm (towers maxed, enemies
active) for the traced window, and trace for 4–9 minutes (or until game
over). Sessions D and E extend this to the late game (waves 36+, up to 4×
speed per the capture session): the per-frame costs of section 3 are the
waves-36+ reference, and the `Enemies.tick` growth (watch item 1) is the
difference to expect. The tables above are the reference; per-session
variance follows the wave mix.

### 5.2 Analyze

```bash
# sample census: busy share, Mibo.Adaptive share, allocation/string callers
# --adaptive defaults to Mibo.Adaptive; 2D-era traces: --adaptive AdaptiveSlop.Core
dotnet fsi tools/analyze-trace.fsx <trace.speedscope.json>

# structure: gap histogram, vsync cadence (the frame-count truth), CPU_TIME spans
dotnet fsi tools/probe-structure.fsx <trace.speedscope.json>

# per-minute busy/adaptive ramp
dotnet fsi tools/probe-minute.fsx <trace.speedscope.json>

# subtree + child attribution; queries are positional argv
dotnet fsi tools/analyze-subtree.fsx <trace.speedscope.json> "Mibo.Adaptive" "Towers.tick" "Application.update" "WorldView"

# per-function open rates; markers are argv fragments
# raylib: --draw "Renderer3D" --update "StepCore"
dotnet fsi trace-count.fsx <trace.speedscope.json>

# GC counts + stop-the-world pauses (needs the .nettrace)
dotnet run --project tools/gcprobe -- <trace.nettrace>
```

Expected output that matches the tables:

- MonoGame backends: vsync cadence k=1 ≈ 81–97 % (60 Hz), busy ≈ 9–15 % of
  wall, Mibo.Adaptive ≈ 0.07 ms/frame (0.11 in the waves-36+ Raylib session),
  GC ≈ 0.9–1.2/s with max STW ≈ 1.0–1.5 ms.
- Raylib: cadence k=1 ≈ 32–63 % / k=2 ≈ 33–49 % (~30 Hz pacing), busy ≈
  4.4–6.6 %, Mibo.Adaptive ≈ 0.09–0.11 ms/frame, GC ≈ 0.44–0.60/s with max
  STW < 0.8 ms.

All tools are backend- and namespace-agnostic (filters pass through argv).
The vsync cadence of `probe-structure.fsx` is the frame-count source of
truth; the open counts of `trace-count.fsx` are sample runs, not frame
counts (section 1).

## 6. Project status

Sim core, backends (Raylib, MonoDX12/MonoDX11/MonoVK/MonoGL), the Content
pipeline and the model dataset are in place. Five capture sessions were
analyzed for this assessment: MonoDX12 (three sessions) and Raylib (two
sessions), each captured per section 5. Sessions D and E validate the
current commit (presenter views, frame-carried clock, zero-copy model
parts) at waves 36+; the `Enemies.tick` buffer-growth item (watch item 1)
is the follow-up. The remaining backends and the test suite are next.
