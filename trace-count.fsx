// trace-count.fsx
//
// Usage: dotnet fsi trace-count.fsx <trace.speedscope.json> [--tail <fraction>]
//
// Counts "O" (open) events per frame name on the MAIN thread — i.e. how many
// times each function was entered in the window. Used to derive frames/sec
// and per-frame call counts (draw calls, constant-buffer applications, etc).

open System
open System.IO
open System.Text.Json
open System.Collections.Generic

let path =
  if fsi.CommandLineArgs.Length < 2 then
    eprintfn
      "usage: dotnet fsi trace-count.fsx <trace.speedscope.json> [--tail <fraction>]"

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

let parseFrames(arr: JsonElement) : string[] =
  (arr.EnumerateArray() |> Seq.toArray)
  |> Array.map(fun f -> let s = getString f "name" in if s = "" then "?" else s)

let sharedFrames =
  match getProp root "shared" with
  | Some sh ->
    match getProp sh "frames" with
    | Some f -> Some(parseFrames f)
    | _ -> None
  | _ -> None

let profiles = root.GetProperty("profiles").EnumerateArray() |> Seq.toArray

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

let prof = profiles[mainIdx]

let frames =
  match getProp prof "frames" with
  | Some f when f.GetArrayLength() > 0 -> parseFrames f
  | _ -> sharedFrames |> Option.defaultValue [||]

let startV = prof.GetProperty("startValue").GetDouble()
let endV = prof.GetProperty("endValue").GetDouble()

let cutoff =
  match tailFraction with
  | Some f -> startV + (endV - startV) * (1.0 - f)
  | None -> startV

let counts = Dictionary<string, int64>()

for e in prof.GetProperty("events").EnumerateArray() do
  let at = e.GetProperty("at").GetDouble()

  if at >= cutoff && getString e "type" = "O" then
    let name = frames[e.GetProperty("frame").GetInt32()]

    match counts.TryGetValue name with
    | true, v -> counts[name] <- v + 1L
    | _ -> counts[name] <- 1L

// speedscope evented timestamps are in milliseconds
let windowSec = (endV - cutoff) / 1000.0

printfn "file: %s" (Path.GetFileName path)

printfn
  "window: %.1f s (%s)"
  windowSec
  (match tailFraction with
   | Some f -> $"last {f * 100.0}%%"
   | None -> "full")

let draws =
  counts
  |> Seq.tryPick(fun kv ->
    if kv.Key.Contains "MiboGame`2" && kv.Key.Contains ".Draw(" then
      Some kv.Value
    else
      None)
  |> Option.defaultValue 0L

let updates =
  counts
  |> Seq.tryPick(fun kv ->
    if kv.Key.Contains "Game.DoUpdate" then
      Some kv.Value
    else
      None)
  |> Option.defaultValue 0L

printfn
  "frames (MiboGame.Draw opens): %d  -> %.1f fps"
  draws
  (float draws / windowSec)

printfn
  "updates (Game.DoUpdate opens): %d  -> %.1f ups"
  updates
  (float updates / windowSec)

printfn ""
printfn "  per-frame    total   /sec  name"

let interesting(kv: KeyValuePair<string, int64>) =
  let n = kv.Key

  n.Contains "Pipelines"
  || n.Contains "DrawInstancedPrimitives"
  || n.Contains "ConstantBuffer"
  || n.Contains "EffectPass"
  || n.Contains "SetData"
  || n.Contains "SetRenderTarget"
  || n.Contains "ApplyState"
  || n.Contains "DrawMesh"
  || n.Contains "Palette"
  || n.Contains "Present"
  || n.Contains "Clear"
  || n.Contains "EffectParameter.SetValue"
  || n.Contains "DrawUserIndexed"
  || n.Contains "DrawIndexedPrimitives"

counts
|> Seq.filter interesting
|> Seq.sortByDescending(fun kv -> kv.Value)
|> Seq.iter(fun kv ->
  let perFrame = if draws > 0L then float kv.Value / float draws else 0.0

  printfn
    "  %9.2f  %8d  %7.1f  %s"
    perFrame
    kv.Value
    (float kv.Value / windowSec)
    kv.Key)
