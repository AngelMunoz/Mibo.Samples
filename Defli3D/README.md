# Defli3D — Adaptive Architecture Trace Assessment (3D)

**Date:** 2026-08-13
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
- The missed-vsync tail (k=2, k=3) is the trace collector's doing. The user
  reports the tracer causes hitches; the games hold their pacing outside
  those. Per-frame numbers below use nominal frame counts (60 Hz for the
  MonoGame traces, observed ~30 Hz for raylib).

## 2. Macroscopic — what the game is doing

The game is not CPU-bound. The thread is busy 4.4–13 % of wall time. The
frame pace is set by the renderer path, not by the simulation.

| Metric | 2D post-joinOn | A (MonoDX12) | B (Raylib) | C (MonoDX12) |
| --- | --- | --- | --- | --- |
| Busy per frame | 0.55 ms | 2.16 ms | 1.48 ms | 1.55 ms |
| Update side | ~47 % (pre-joinOn) | 20.5 % | 42.3 % (StepCore) | 26.6 % |
| Draw side | 6.5 % | 65.2 % | 41.5 % (Renderer3D) | 58.7 % (DoDraw) |
| zeroCreate samples | 1 219 | 5 429 | 6 660 | 10 898 |
| GCs / STW max | 188 / 0.69 ms | 271 / 1.40 ms | 212 / 0.79 ms | 499 / 1.49 ms |

Draw-side composition (share of busy samples):

- **A:** `WorldView.worldView` 76.7 % of draw (MapView + volume instancing
  31.4 %, InstanceScratch 26.2 %, `ModelCache.boneOf` 13.3 %, TowersView
  11.4 %). Native (`CPU_TIME`) is 67 % of draw — the DX12/GPU path.
- **B:** `WorldView.worldView` 39.2 % (TowersView 13.5 %, ProjectilesView
  7.8 %, EnemiesView 7.3 %, VfxView 1.2 %). The raylib backend has **no
  per-frame MapView pass** and its pipeline `Execute` is only 1.3 % of busy
  (vs 8.0 % in C). Its CPU draw cost is the cheapest of the three.
- **C:** `WorldView.worldView` 43.5 % (MapView 13.1 %, TowersView 7.9 %,
  ProjectilesView 5.1 %, EnemiesView 3.8 %, hoverOverlays 2.1 %), pipeline
  `Execute` 8.0 %, HUD 0.5 %.

Cadence observations:

- The MonoGame backends hold 60 Hz for most frames (A: k=1 91.3 %, C: k=1
  81.0 %). The k≥2 tail (5.7 % / 19 %) is the tracer hitch pattern.
- Raylib is paced at ~30 Hz in this environment (k=1 32.2 %, k=2 49.4 %).
  Its CPU busy is only 4.4 %, so this is not CPU work — it is the
  present/GL pacing of the environment. Confirm on GPU hardware.
- Trace A had eight native busy-stalls of 0.9–2.2 s inside Draw, spaced
  ~30–35 s apart (wave cadence). They contain no sim and no adaptive frames.
  They are first-use pipeline/shader work or WARP-class rasterization; a
  no-collector session on GPU hardware would attribute them.

## 3. Microscopic — what the simulation is doing

Per-frame sim cost (all three traces match the 2D session almost exactly):

| Item | A (÷14 553) | B (÷14 470) | C (÷33 419) | 2D post-joinOn |
| --- | --- | --- | --- | --- |
| `Towers.tick` | 0.177 ms | 0.225 ms | 0.167 ms | 0.159 ms |
| `Projectiles.tick` (homing) | 0.135 ms | 0.129 ms | 0.107 ms | 0.114 ms |
| `Enemies.tick` | 0.061 ms | 0.074 ms | 0.054 ms | ~0.05 ms |
| **Mibo.Adaptive machinery** | **0.074 ms** | **0.088 ms** | **0.066 ms** | **0.18 ms** |
| Frame budget used (16.7 ms) | 13.0 % | 8.9 % | 9.3 % | 3.3 % |

- **Mibo.Adaptive is 0.066–0.088 ms/frame across all three 3D backends —
  2–2.7× cheaper per frame than the 2D post-joinOn (0.18 ms).**
- All adaptive samples are in the update side. The draw side reads the
  packed `RenderFrame` and touches zero adaptive frames (0 samples in draw).
- The adaptive work is the read-time gate: per-key node reads
  (`AdaptiveNode<float>` cooldownA 0.9–1.8 % of busy, `voption<int>`
  targetA 0.4–0.9 %, `EnemyView` reads 0.6 %, `MapLookupNode` Motion/EnemyDef
  0.2–1.0 %), `FilterMapNode.GetValue` 0.5–1.0 %, the Homing
  `JoinMapNode.CreateEntry` 0.3 %.
- The write side is dead: `pushMapDelta`/`OnDeltas` ≈ 0 samples;
  `CommitJournal` 1 sample in C; `ChangeableMap.Apply` 8 samples in C.
  `diffSubscriptions` is 139 samples (0.6 %) in B, 86 in A.
- Telemetry (session A, 61 236 forced frames, game over at the end, 0 paused
  frames): per-frame element recomputes are cheap version checks — Homing
  join 24.7/frame (≈25 projectiles in flight), Suppression 17.9/frame,
  Views join 9.2/frame, Alive filter 8.5/frame, BossPositions 8.3/frame;
  upgrade/hover chains are rare (EffectiveDef 0.09, RangeRing 0.015,
  PlacementPreview 0.034, Banner 0.001, GameOver 0.0003 per frame). The
  measured cost of all of it is the 0.07 ms/frame above.
- Allocation drip: sim-side zeroCreate is flat and linear in entities
  (adaptive `Recompute` arrays: 750+454+17 in C ≈ 1 224 samples, 2.4 % of
  busy). The renderer owns the allocations: `InstanceScratch` is 88.5–89.1 %
  of every trace's zeroCreate (A 4 828, B 5 937, C 9 646 samples) — the
  per-group doubling growth during the entity ramp.
- GC (from the nettrace, `tools/gcprobe`): all pauses < 1.5 ms (A max 1.40,
  B max 0.79, C max 1.49; 98–100 % under 1 ms). Raylib (0.44/s) matches the
  2D post-joinOn rate (0.33/s); MonoGame DX12 allocates more (0.90–1.12/s)
  and pays ~2.7× the GC frequency. STW totals: A 148 ms, B 65 ms, C 263 ms
  over their sessions. GC cannot be felt: the worst pause is 9 % of one
  frame budget.

## 4. Verdict

- **Mibo.Adaptive does not drag the game.** It costs 0.07–0.09 ms/frame
  (≈0.4–0.5 % of the 16.7 ms budget) — cheaper per frame than the validated
  2D post-joinOn baseline (0.18 ms/frame), with a dead write side and flat
  allocations.
- **The simulation matches the 2D per-frame cost almost exactly.** The same
  code, the same wave range, the same per-frame behavior on three backends.
- **The renderer owns the frame.** Draw is 41–65 % of busy; the sim is
  21–42 %. The `InstanceScratch` growth is the top allocation source and the
  GC driver. The MonoGame draw path carries a large native component
  (`CPU_TIME`) that the raylib path does not.
- **The frame pace is environmental, not architectural.** MonoGame holds
  60 Hz; raylib paces at ~30 Hz in this capture. Both have collector hitches
  in the k≥2 cadence tail. Nothing in the trace points at the sim or the
  adaptive graph for a missed frame.

Watch items, in order:

1. **InstanceScratch growth** (0.9–1.7 zeroCreate samples/frame, 88–89 % of
   allocations) — pool or pre-size the per-group arrays; it drives the GC
   rate.
2. **Native draw path in MonoGame** (CPU_TIME 67 % of draw in A) — validate
   on GPU hardware; the 1–2 s wave-cadence stalls are the same unknown.
3. **Raylib 30 Hz pacing** — confirm vsync/target-fps behavior on real
   hardware; the CPU is 4.4 % busy, so nothing in the sim explains it.
4. **HUD strings** — raylib `Path.Combine` (AssetsService.resolvePath) 222
   samples + `PrintFormatToString` 74; MonoGame `PrintFormatToStringThen` 84.
   View-side micro-optimization, not an adaptive issue.
5. **Homing join `CreateEntry` / per-key reads** — still the largest adaptive
   line (0.3 % of busy) and the one that scales with projectile volume
   (24.7/frame in the telemetry session).

## 5. Reproduction

```bash
# sample census + adaptive share + allocation/string callers
dotnet fsi tools/analyze-trace.fsx <trace.speedscope.json> [--adaptive Mibo.Adaptive]

# subtree + child attribution; queries are positional argv
dotnet fsi tools/analyze-subtree.fsx <trace.speedscope.json> [query ...]

# structure: gap histogram, vsync cadence (frame-count truth), CPU_TIME spans
dotnet fsi tools/probe-structure.fsx <trace.speedscope.json> [--vsync 16.6667]

# per-minute busy/adaptive ramp
dotnet fsi tools/probe-minute.fsx <trace.speedscope.json> [--adaptive Mibo.Adaptive]

# per-function open rates; markers are argv fragments
dotnet fsi trace-count.fsx <trace.speedscope.json> [--draw <frag>] [--update <frag>] [--interesting <frag>]...

# GC lifecycle from the nettrace
dotnet run --project tools/gcprobe -- <trace.nettrace>
```

The tools are backend- and namespace-agnostic: pass `--adaptive AdaptiveSlop.Core`
for 2D-era traces, `--draw/--update` fragments for any loop shape. Traces were
collected with `dotnet-trace collect --profile gc-verbose -o out.nettrace --name <exe>`
and converted with `dotnet-trace convert --format Speedscope`.

## 6. Project status

Sim core, backends (Raylib, MonoDX12/MonoDX11/MonoVK/MonoGL), the Content
pipeline and the model dataset are in place. The three traces above are the
first 3D captures: MonoDX12 (two sessions) and Raylib (one session). The
remaining backends and the test suite are next.
