// trace-query.fsx
//
// Usage: dotnet fsi trace-query.fsx <trace.speedscope.json> [--tail <fraction>]
//
// Companion to analyze-speedscope.fsx: same aggregation, but the report is a
// full dump of render/pipeline-related frames on the MAIN thread, plus
// non-overlapping self-time category sums. Built to answer "where does the
// forward pipeline (PBR + shadow pass) spend main-thread time".

open System
open System.IO
open System.Text.Json
open System.Collections.Generic

let path =
  if fsi.CommandLineArgs.Length < 2 then
    eprintfn
      "usage: dotnet fsi trace-query.fsx <trace.speedscope.json> [--tail <fraction>]"

    exit 1
  else
    fsi.CommandLineArgs[1]

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

let doc = JsonDocument.Parse(File.ReadAllBytes path)
let root = doc.RootElement

let getProp (el: JsonElement) (name: string) =
  match el.TryGetProperty name with
  | true, v -> Some v
  | _ -> None

let getString (el: JsonElement) (name: string) =
  match getProp el name with
  | Some v when v.ValueKind = JsonValueKind.String ->
    let s = v.GetString()
    if isNull s then "" else s
  | _ -> ""

type Frame = { Index: int; Name: string }

type FrameStat = {
  mutable Self: float
  mutable Inclusive: float
}

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

let parseFrames(arr: JsonElement) : Frame[] =
  (arr.EnumerateArray() |> Seq.toArray)
  |> Array.mapi(fun i f -> {
    Index = i
    Name = let s = getString f "name" in if s = "" then "?" else s
  })

let sharedFrames =
  match getProp root "shared" with
  | Some sh ->
    match getProp sh "frames" with
    | Some f -> Some(parseFrames f)
    | _ -> None
  | _ -> None

let profiles = root.GetProperty("profiles").EnumerateArray() |> Seq.toArray

let mainFrameStats = Dictionary<Frame, FrameStat>()
let mainNameStats = Dictionary<string, FrameStat>()
// Per-name split of self time into managed (CPU_TIME leaf) vs native
// (UNMANAGED_CODE_TIME leaf) — shows whether a hotspot burns managed CPU or
// sits inside a native/driver call.
let mainNameSelfCpu = Dictionary<string, float>()
let mainNameSelfNative = Dictionary<string, float>()
let mutable mainThreadName = ""
let mutable mainThreadTotal = 0.0
let mutable mainCpuTime = 0.0
let mutable mainNativeTime = 0.0

let isSyntheticLeaf(name: string) =
  name = "CPU_TIME" || name = "UNMANAGED_CODE_TIME"

let effectiveSelfIdx (frames: Frame[]) (path: int[]) =
  let mutable idx = 0

  while idx < path.Length - 1 && isSyntheticLeaf frames[path[idx]].Name do
    idx <- idx + 1

  idx

let aggregateEvented (prof: JsonElement) (frames: Frame[]) (isMain: bool) =
  let startV = prof.GetProperty("startValue").GetDouble()
  let endV = prof.GetProperty("endValue").GetDouble()
  let evs = prof.GetProperty("events")

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
      let path = stack |> Seq.toArray
      let selfIdx = effectiveSelfIdx frames path

      if isMain then
        bump mainFrameStats frames[path[selfIdx]] dt dt
        bump mainNameStats frames[path[selfIdx]].Name dt dt

        let trueLeafName = frames[path[0]].Name

        if trueLeafName = "CPU_TIME" then
          mainCpuTime <- mainCpuTime + dt
          let n = frames[path[selfIdx]].Name

          mainNameSelfCpu[n] <-
            (match mainNameSelfCpu.TryGetValue n with
             | true, v -> v
             | _ -> 0.0)
            + dt
        elif trueLeafName = "UNMANAGED_CODE_TIME" then
          mainNativeTime <- mainNativeTime + dt
          let n = frames[path[selfIdx]].Name

          mainNameSelfNative[n] <-
            (match mainNameSelfNative.TryGetValue n with
             | true, v -> v
             | _ -> 0.0)
            + dt

        for j = 0 to path.Length - 1 do
          if j <> selfIdx then
            bump mainFrameStats frames[path[j]] 0.0 dt
            bump mainNameStats frames[path[j]].Name 0.0 dt

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
  total

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
  let isMain = i = mainIdx

  let frames =
    match getProp prof "frames" with
    | Some f when f.GetArrayLength() > 0 -> parseFrames f
    | _ ->
      match sharedFrames with
      | Some f -> f
      | _ -> [||]

  let total =
    if getString prof "type" = "evented" then
      aggregateEvented prof frames isMain
    else
      0.0

  if isMain then
    mainThreadName <- getString prof "name"
    mainThreadTotal <- total

// ── report ──────────────────────────────────────────────────────────────────

let mainTotal = max 1e-9 mainThreadTotal
let pct v = 100.0 * v / mainTotal

printfn "file: %s" (Path.GetFileName path)

printfn
  "tail window: %s"
  (match tailFraction with
   | Some f -> $"last {f * 100.0}%%"
   | None -> "full")

printfn
  "main thread: %s  wall=%.1f ms  managed=%.1f%%  native=%.1f%%"
  mainThreadName
  mainThreadTotal
  (100.0 * mainCpuTime / mainTotal)
  (100.0 * mainNativeTime / mainTotal)

// Non-overlapping self-time buckets (main thread). Every frame lands in
// exactly one bucket, checked in order.
let buckets: (string * (string -> bool))[] = [|
  "Present/GraphicsDevice.Present",
  fun n -> n.Contains "PlatformPresent" || n.Contains "GraphicsDevice.Present"
  "Constant buffers + state apply",
  fun n ->
    n.Contains "ConstantBuffer"
    || n.Contains "PlatformApplyState"
    || n.Contains ".ApplyState"
    || n.Contains "EffectPass"
    || n.Contains "PlatformApplyPass"
    || n.Contains "ApplyRenderTarget"
    || n.Contains "SetRenderTarget"
    || n.Contains "PlatformApplyDefaultRenderTarget"
  "Draw API (Draw*Primitives/DrawMesh*)",
  fun n ->
    n.Contains "DrawInstancedPrimitives"
    || n.Contains "DrawPrimitives"
    || n.Contains "DrawUserPrimitives"
    || n.Contains "DrawMeshInstanced"
    || n.Contains "DrawMesh"
    || n.Contains "DrawModel"
  "GPU uploads (SetData/UpdateSubresource/CreateTexture)",
  fun n ->
    n.Contains "SetDataInternal"
    || n.Contains "SetData"
    || n.Contains "UpdateSubresource"
    || n.Contains "CreateTexture"
    || n.Contains "uploadPaletteChunk"
    || n.Contains "SetTexture"
    || n.Contains "UpdateTexture"
    || n.Contains "LoadTexture"
  "Mibo pipeline (Pipelines/Pbr/Shading/Shadow/Renderer3D/RenderBuffer)",
  fun n ->
    n.Contains "Pipelines"
    || n.Contains "PbrShading"
    || n.Contains "Shading"
    || n.Contains "Shadow"
    || n.Contains "Renderer3D"
    || n.Contains "RenderBuffer"
    || n.Contains "RenderTargetPool"
  "Mibo core elmish/renderer glue",
  fun n -> n.Contains "Mibo.Elmish" || n.Contains "MiboGame"
  "Mibo animation (CPU skinning/pose)", fun n -> n.Contains "Mibo.Animation"
  "Sample game code (AnimatedInstancing.*)",
  fun n -> n.Contains "AnimatedInstancing"
  "GC / runtime bookkeeping",
  fun n ->
    n.Contains "PollGC"
    || n.Contains "GarbageCollect"
    || n.Contains "GC_"
    || n.Contains "WaitForGC"
  "Thread sync/wait",
  fun n ->
    n.Contains "Monitor"
    || n.Contains "SpinWait"
    || n.Contains "Thread.Sleep"
    || n.Contains "WaitOne"
    || n.Contains "ManualResetEvent"
    || n.Contains "Semaphore"
    || n.Contains "SpinOnce"
|]

let bucketTotals = Array.zeroCreate<float> buckets.Length
let mutable bucketOther = 0.0

for kv in mainNameStats do
  let name = kv.Key
  let mutable placed = false
  let mutable bi = 0

  while not placed && bi < buckets.Length do
    if (snd buckets[bi]) name then
      bucketTotals[bi] <- bucketTotals[bi] + kv.Value.Self
      placed <- true
    else
      bi <- bi + 1

  if not placed then
    bucketOther <- bucketOther + kv.Value.Self

printfn "\n--- SELF-TIME CATEGORIES (main thread, non-overlapping) ---"

for i = 0 to buckets.Length - 1 do
  if bucketTotals[i] > 0.0 then
    printfn
      "  %6.2f%%  %9.1f ms  %s"
      (pct bucketTotals[i])
      bucketTotals[i]
      (fst buckets[i])

printfn "  %6.2f%%  %9.1f ms  (other)" (pct bucketOther) bucketOther

// Frames of interest: everything render/pipeline related, by inclusive time.
let interesting(name: string) =
  name.Contains "Pipelines"
  || name.Contains "PbrShading"
  || name.Contains "Shadow"
  || name.Contains "Shade"
  || name.Contains "Renderer3D"
  || name.Contains "RenderBuffer"
  || name.Contains "RenderTarget"
  || name.Contains "ConstantBuffer"
  || name.Contains "ApplyState"
  || name.Contains "ApplyPass"
  || name.Contains "EffectPass"
  || name.Contains "DrawInstancedPrimitives"
  || name.Contains "DrawPrimitives"
  || name.Contains "DrawMesh"
  || name.Contains "Present"
  || name.Contains "SetData"
  || name.Contains "UpdateSubresource"
  || name.Contains "CreateTexture"
  || name.Contains "Palette"
  || name.Contains "Instanced"
  || name.Contains "Clear"
  || name.Contains "Effect"
  || name.Contains "Viewport"

printfn
  "\n--- RENDER/PIPELINE FRAMES ON MAIN THREAD (incl desc; incl >= 1 ms) ---"

printfn "  self%%   self ms  (cpu ms / native ms)   incl%%   incl ms  name"

let getSplit (d: Dictionary<string, float>) (n: string) =
  match d.TryGetValue n with
  | true, v -> v
  | _ -> 0.0

mainNameStats
|> Seq.filter(fun kv -> interesting kv.Key && kv.Value.Inclusive >= 1.0)
|> Seq.sortByDescending(fun kv -> kv.Value.Inclusive)
|> Seq.iter(fun kv ->
  printfn
    "  %6.2f  %8.1f  (%8.1f / %8.1f)  %6.2f  %8.1f  %s"
    (pct kv.Value.Self)
    kv.Value.Self
    (getSplit mainNameSelfCpu kv.Key)
    (getSplit mainNameSelfNative kv.Key)
    (pct kv.Value.Inclusive)
    kv.Value.Inclusive
    kv.Key)

printfn
  "\n--- ALL NAMES CONTAINING 'Shadow' (any thread-attributed, main only shown) ---"

mainNameStats
|> Seq.filter(fun kv -> kv.Key.Contains "Shadow" || kv.Key.Contains "shadow")
|> Seq.sortByDescending(fun kv -> kv.Value.Inclusive)
|> Seq.iter(fun kv ->
  printfn
    "  %6.2f  %8.1f  %6.2f  %8.1f  %s"
    (pct kv.Value.Self)
    kv.Value.Self
    (pct kv.Value.Inclusive)
    kv.Value.Inclusive
    kv.Key)

// Distinct frame inventory so nothing render-related hides behind naming.
printfn "\n--- FULL NAME INVENTORY (main-thread incl desc, incl >= 5 ms) ---"

mainNameStats
|> Seq.filter(fun kv -> kv.Value.Inclusive >= 5.0)
|> Seq.sortByDescending(fun kv -> kv.Value.Inclusive)
|> Seq.iter(fun kv ->
  printfn
    "  %6.2f  %8.1f  %6.2f  %8.1f  %s"
    (pct kv.Value.Self)
    kv.Value.Self
    (pct kv.Value.Inclusive)
    kv.Value.Inclusive
    kv.Key)
