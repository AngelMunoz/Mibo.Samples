// analyze-speedscope.fsx
//
// Usage: dotnet fsi analyze-speedscope.fsx <trace.speedscope.json>
//
// Aggregates a dotnet-trace speedscope.json (per-thread evented profiles, with
// a shared frames table) into per-function self/inclusive CPU time, per-thread
// totals, and the hottest full stacks — so a game-frame hotspot can be located
// without opening PerfView/speedscope.
//
// Handles both profile types:
//   - "evented": events[] of open/close (type "O"/"C") with timestamps; time
//     deltas are attributed to the current top-of-stack frame.
//   - "sampled": samples[][] + weights[] (legacy dotnet-trace output).

open System
open System.IO
open System.Text.Json
open System.Collections.Generic

let path =
  if fsi.CommandLineArgs.Length < 2 then
    eprintfn
      "usage: dotnet fsi analyze-speedscope.fsx <trace.speedscope.json> [--tail <fraction>]"

    exit 1
  else
    fsi.CommandLineArgs[1]

/// Optional time-window filter: `--tail 0.25` keeps only the last 25% of the
/// profile timeline (useful to isolate the heaviest phase of a session, e.g.
/// the 10k-instance tier after ramping up crowd size).
let tailFraction =
  fsi.CommandLineArgs
  |> Array.tryFindIndex(fun a -> a = "--tail")
  |> Option.bind(fun i ->
    fsi.CommandLineArgs
    |> Array.tryItem(i + 1)
    |> Option.bind(fun s ->
      match Double.TryParse s with
      | true, v when v > 0.0 && v < 1.0 -> Some v
      | _ -> None))

match tailFraction with
| Some f -> printfn "tail window: last %.0f%% of the timeline" (f * 100.0)
| None -> ()

printfn "reading %s ..." path
let sw = Diagnostics.Stopwatch.StartNew()
let doc = JsonDocument.Parse(File.ReadAllBytes path)
let root = doc.RootElement

let getProp (el: JsonElement) (name: string) =
  match el.TryGetProperty name with
  | true, v -> Some v
  | _ -> None

/// Reads a string property; "" when missing/null/non-string.
let getString (el: JsonElement) (name: string) =
  match getProp el name with
  | Some v when v.ValueKind = JsonValueKind.String ->
    let s = v.GetString()
    if isNull s then "" else s
  | _ -> ""

type Frame = {
  Index: int
  Name: string
  File: string
  Line: int
}

type FrameStat = {
  mutable Self: float
  mutable Inclusive: float
}

let frameStats = Dictionary<Frame, FrameStat>()
let nameStats = Dictionary<string, FrameStat>()

let bump
  (dict: Dictionary<'K, FrameStat>)
  (key: 'K)
  (self: float)
  (inclusive: float)
  =
  match dict.TryGetValue key with
  | true, s ->
    s.Self <- s.Self + self
    s.Inclusive <- s.Inclusive + inclusive
  | _ -> dict[key] <- { Self = self; Inclusive = inclusive }

let renderFrame(f: Frame) =
  if f.File <> "" then
    $"{f.Name}  [{Path.GetFileName f.File}:{f.Line}]"
  else
    f.Name

let parseFrames(arr: JsonElement) : Frame[] =
  (arr.EnumerateArray() |> Seq.toArray)
  |> Array.mapi(fun i f -> {
    Index = i
    Name =
      let s = getString f "name"
      if s = "" then "?" else s
    File = getString f "file"
    Line =
      match getProp f "line" with
      | Some v when v.ValueKind = JsonValueKind.Number -> v.GetInt32()
      | _ -> 0
  })

// Shared frames table (dotnet-trace emits one for all per-thread profiles).
let sharedFrames =
  match getProp root "shared" with
  | Some sh ->
    match getProp sh "frames" with
    | Some f -> Some(parseFrames f)
    | _ -> None
  | _ -> None

let profiles = root.GetProperty("profiles").EnumerateArray() |> Seq.toArray

printfn
  "profiles: %d, shared frames: %d"
  profiles.Length
  (sharedFrames |> Option.map Array.length |> Option.defaultValue 0)

// ── per-profile aggregation ─────────────────────────────────────────────────

let pathCounts =
  Dictionary<int[], float>(
    { new IEqualityComparer<int[]> with
        member _.Equals(a, b) =
          a.Length = b.Length && Array.forall2 (fun x y -> x = y) a b

        member _.GetHashCode(a) =
          let mutable h = 17

          for x in a do
            h <- h * 31 + x

          h
    }
  )

let threadStats = ResizeArray<struct (string * float)>() // (thread name, CPU ms)

// Per-thread breakdown for the busiest (main) thread.
let mainFrameStats = Dictionary<Frame, FrameStat>()
let mainNameStats = Dictionary<string, FrameStat>()
let mutable mainThreadName = ""
let mutable mainThreadTotal = 0.0
let mutable mainCpuTime = 0.0
let mutable mainNativeTime = 0.0

/// dotnet-trace synthetic leaf frames.  In evented traces ALL wall-clock
/// time lands in one of these — they sit at the leaf and represent either
/// managed execution (CPU_TIME) or native/driver/GPU work (UNMANAGED_CODE_TIME).
/// We treat them as transparent: their self time is re-attributed to the
/// nearest non-synthetic ancestor so real methods get meaningful numbers.
let isSyntheticLeaf(name: string) =
  name = "CPU_TIME" || name = "UNMANAGED_CODE_TIME"

/// path is leaf-first (path[0] = top-of-stack = leaf, last = root).
/// Returns the array index of the effective self-frame, skipping leading
/// synthetic leaves (CPU_TIME / UNMANAGED_CODE_TIME).
let effectiveSelfIdx (frames: Frame[]) (path: int[]) =
  let mutable idx = 0

  while idx < path.Length - 1 && isSyntheticLeaf frames[path[idx]].Name do
    idx <- idx + 1

  idx

let bumpMain (frames: Frame[]) (path: int[]) (dt: float) (selfIdx: int) =
  bump mainFrameStats frames[path[selfIdx]] dt dt
  bump mainNameStats frames[path[selfIdx]].Name dt dt

  for j = 0 to path.Length - 1 do
    if j <> selfIdx then
      bump mainFrameStats frames[path[j]] 0.0 dt
      bump mainNameStats frames[path[j]].Name 0.0 dt

let bumpPath (path: int[]) (dt: float) =
  match pathCounts.TryGetValue path with
  | true, acc -> pathCounts[path] <- acc + dt
  | _ -> pathCounts[path] <- dt

let aggregateEvented (prof: JsonElement) (frames: Frame[]) (isMain: bool) =
  let tname = getString prof "name"
  let startV = prof.GetProperty("startValue").GetDouble()
  let endV = prof.GetProperty("endValue").GetDouble()
  let evs = prof.GetProperty("events")

  // Cutoff timestamp for --tail: intervals before it are skipped entirely.
  let cutoff =
    match tailFraction with
    | Some f -> startV + (endV - startV) * (1.0 - f)
    | None -> startV

  let stack = Stack<int>()
  let mutable prev = startV
  let mutable total = 0.0

  let attr (dt: float) (intervalStart: float) =
    let dt = max 0.0 (intervalStart + dt - max intervalStart cutoff)

    if dt > 0.0 && stack.Count > 0 then
      // Stack enumerates leaf-first (top-of-stack = path[0]).
      let path = stack |> Seq.toArray
      let selfIdx = effectiveSelfIdx frames path

      bump frameStats frames[path[selfIdx]] dt dt
      bump nameStats frames[path[selfIdx]].Name dt dt

      bumpPath path dt

      if isMain then
        bumpMain frames path dt selfIdx
        // Track managed vs native breakdown (true leaf = path[0])
        let trueLeafName = frames[path[0]].Name

        if trueLeafName = "CPU_TIME" then
          mainCpuTime <- mainCpuTime + dt
        elif trueLeafName = "UNMANAGED_CODE_TIME" then
          mainNativeTime <- mainNativeTime + dt

      for j = 0 to path.Length - 1 do
        if j <> selfIdx then
          bump frameStats frames[path[j]] 0.0 dt
          bump nameStats frames[path[j]].Name 0.0 dt

      total <- total + dt

  for e in evs.EnumerateArray() do
    let at = e.GetProperty("at").GetDouble()
    attr (at - prev) prev
    let ty = getString e "type"

    if ty = "O" then
      stack.Push(e.GetProperty("frame").GetInt32())
    elif ty = "C" && stack.Count > 0 then
      stack.Pop() |> ignore

    prev <- at

  attr (endV - prev) prev
  threadStats.Add(struct (tname, total))
  printfn "  %-42s cpu=%.1f ms" tname total
  total

let aggregateSampled (prof: JsonElement) (frames: Frame[]) =
  let tname = getString prof "name"
  let samplesEl = prof.GetProperty("samples")
  let sampleCount = samplesEl.GetArrayLength()

  let weights =
    match getProp prof "weights" with
    | Some w when w.GetArrayLength() = sampleCount -> [|
        for i in 0 .. sampleCount - 1 -> w[i].GetDouble()
      |]
    | _ -> [| for _ in 0 .. sampleCount - 1 -> 1.0 |]

  let samples = [|
    for s in samplesEl.EnumerateArray() ->
      [| for i in 0 .. s.GetArrayLength() - 1 -> s[i].GetInt32() |]
  |]

  let mutable total = 0.0

  for i in 0 .. sampleCount - 1 do
    let s = samples[i]
    let w = weights[i]

    if s.Length > 0 then
      // speedscope sampled format is root-first (index 0 = root, last = leaf).
      // Reverse to leaf-first to match the evented path convention so
      // effectiveSelfIdx and bumpPath work uniformly.
      let path = Array.rev s
      let selfIdx = effectiveSelfIdx frames path

      bump frameStats frames[path[selfIdx]] w w
      bump nameStats frames[path[selfIdx]].Name w w
      bumpPath path w

      for j = 0 to path.Length - 1 do
        if j <> selfIdx then
          bump frameStats frames[path[j]] 0.0 w
          bump nameStats frames[path[j]].Name 0.0 w

      total <- total + w

  threadStats.Add(struct (tname, total))
  printfn "  %-42s cpu=%.1f ms" tname total
  total

// The busiest thread (most events) is the main/game thread.
let mainIdx =
  profiles
  |> Array.mapi(fun i p ->
    let n =
      match getProp p "events" with
      | Some e -> e.GetArrayLength()
      | _ -> 0

    struct (i, n))
  |> Array.maxBy(fun struct (_, n) -> n)
  |> fun struct (i, _) -> i

for i = 0 to profiles.Length - 1 do
  let prof = profiles[i]
  let ptype = getString prof "type"
  let isMain = i = mainIdx

  let frames =
    match getProp prof "frames" with
    | Some f when f.GetArrayLength() > 0 -> parseFrames f
    | _ ->
      match sharedFrames with
      | Some f -> f
      | _ ->
        eprintfn "no frames for profile"
        [||]

  let total =
    if ptype = "evented" then
      aggregateEvented prof frames isMain
    elif ptype = "sampled" then
      aggregateSampled prof frames
    else
      0.0

  if isMain then
    mainThreadName <- getString prof "name"
    mainThreadTotal <- total

// ── report ──────────────────────────────────────────────────────────────────

// Structural trace frames that aren't real methods — filtered from
// "top by self time" displays.  CPU_TIME / UNMANAGED_CODE_TIME are handled
// as transparent leaves during aggregation (see effectiveSelfIdx), so they
// will not appear with meaningful self time here regardless.
let isMarker(name: string) =
  name = "UNMANAGED_CODE_TIME"
  || name = "CPU_TIME"
  || name = "Threads"
  || name = "(Non-Activities)"
  || name.StartsWith "Thread ("
  || name.StartsWith "Process64"

let totalAll = frameStats.Values |> Seq.sumBy(fun s -> s.Self)
let total = max 1e-9 totalAll
let pct v = 100.0 * v / total

let markerTotal =
  frameStats
  |> Seq.filter(fun kv -> isMarker kv.Key.Name)
  |> Seq.sumBy(fun kv -> kv.Value.Self)

printfn "\n=== PER-THREAD CPU TIME (%.1f ms total sampled) ===" totalAll

threadStats
|> Seq.sortByDescending(fun struct (_, t) -> t)
|> Seq.iter(fun struct (name, t) ->
  printfn "  %6.1f ms (%5.2f%%)  %s" t (100.0 * t / total) name)

// ── main thread (game loop) ─────────────────────────────────────────────────

let mainTotal = max 1e-9 mainThreadTotal
let mainPct v = 100.0 * v / mainTotal

let mainMarkerTotal =
  mainFrameStats
  |> Seq.filter(fun kv -> isMarker kv.Key.Name)
  |> Seq.sumBy(fun kv -> kv.Value.Self)

printfn
  "\n=== MAIN THREAD '%s' (%.1f ms wall; managed=%.1f%%  native/gpu=%.1f%%) ==="
  mainThreadName
  mainThreadTotal
  (100.0 * mainCpuTime / mainTotal)
  (100.0 * mainNativeTime / mainTotal)

printfn "--- TOP 20 SELF TIME (managed) ---"

mainFrameStats
|> Seq.filter(fun kv -> not(isMarker kv.Key.Name))
|> Seq.sortByDescending(fun kv -> kv.Value.Self)
|> Seq.truncate 20
|> Seq.iteri(fun i kv ->
  printfn
    "%2d. %6.2f%%  %7.1f ms  %s"
    (i + 1)
    (mainPct kv.Value.Self)
    kv.Value.Self
    (renderFrame kv.Key))

printfn "--- TOP 20 INCLUSIVE TIME (managed) ---"

mainFrameStats
|> Seq.filter(fun kv -> not(isMarker kv.Key.Name))
|> Seq.sortByDescending(fun kv -> kv.Value.Inclusive)
|> Seq.truncate 20
|> Seq.iteri(fun i kv ->
  printfn
    "%2d. %6.2f%%  %7.1f ms  %s"
    (i + 1)
    (mainPct kv.Value.Inclusive)
    kv.Value.Inclusive
    (renderFrame kv.Key))

// ── all threads, markers reported separately ────────────────────────────────

printfn
  "\n=== TOP 25 BY SELF TIME (all threads, markers excluded; markers total %.1f%%) ==="
  (100.0 * markerTotal / total)

frameStats
|> Seq.filter(fun kv -> not(isMarker kv.Key.Name))
|> Seq.sortByDescending(fun kv -> kv.Value.Self)
|> Seq.truncate 25
|> Seq.iteri(fun i kv ->
  printfn
    "%2d. %6.2f%%  %7.1f ms  %s"
    (i + 1)
    (pct kv.Value.Self)
    kv.Value.Self
    (renderFrame kv.Key))

printfn "\n=== TOP 25 BY FUNCTION NAME (all threads, markers excluded) ==="

nameStats
|> Seq.filter(fun kv -> not(isMarker kv.Key))
|> Seq.sortByDescending(fun kv -> kv.Value.Inclusive)
|> Seq.truncate 25
|> Seq.iteri(fun i kv ->
  printfn
    "%2d. %6.2f%% self  %6.2f%% incl  %7.1f ms  %s"
    (i + 1)
    (pct kv.Value.Self)
    (pct kv.Value.Inclusive)
    kv.Value.Inclusive
    kv.Key)

printfn "\n=== TOP 12 HOTTEST STACKS (root → leaf) ==="

pathCounts
|> Seq.filter(fun kv ->
  let frames = sharedFrames |> Option.defaultValue [||]
  let names = kv.Key |> Array.map(fun fi -> frames[fi].Name)
  names |> Array.exists(fun n -> not(isMarker n)))
|> Seq.sortByDescending(fun kv -> kv.Value)
|> Seq.truncate 12
|> Seq.iteri(fun i kv ->
  let frames = sharedFrames |> Option.defaultValue [||]
  // path is stored leaf-first; reverse for root→leaf display
  let stack =
    kv.Key
    |> Array.rev
    |> Array.map(fun fi -> frames[fi].Name)
    |> String.concat " → "

  printfn "%2d. %6.2f%%  %7.1f ms  %s" (i + 1) (pct kv.Value) kv.Value stack)

printfn
  "\n(%d distinct frames, %d distinct names, %d distinct stacks)"
  frameStats.Count
  nameStats.Count
  pathCounts.Count

printfn "done in %.1fs" sw.Elapsed.TotalSeconds
