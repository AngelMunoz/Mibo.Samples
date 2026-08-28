#r "C:/Users/scyth/.nuget/packages/assimpnetter/6.0.4/lib/net6.0/AssimpNetter.dll"

open Assimp

let path = fsi.CommandLineArgs[1]

let ctx = AssimpContext()

let flags =
  PostProcessSteps.FindDegenerates
  ||| PostProcessSteps.FindInvalidData
  ||| PostProcessSteps.JoinIdenticalVertices
  ||| PostProcessSteps.Triangulate

let scene = ctx.ImportFile(path, flags)

let printM(m: Matrix4x4) =
  $"| {m.A1:g3} {m.A2:g3} {m.A3:g3} {m.A4:g3} | {m.B1:g3} {m.B2:g3} {m.B3:g3} {m.B4:g3} | {m.C1:g3} {m.C2:g3} {m.C3:g3} {m.C4:g3} | {m.D1:g3} {m.D2:g3} {m.D3:g3} {m.D4:g3} |"

let mutable skinned: Mesh = null

for m in scene.Meshes do
  if isNull skinned && m.HasBones then
    skinned <- m

let rec findNode (name: string) (n: Node) : Node =
  if n.Name = name then
    n
  else
    let mutable r: Node = null

    for c in n.Children do
      if isNull r then
        r <- findNode name c

    r

printfn "=== BONES: bind-local node transform (translation = A4,B4,C4) ==="

for b in skinned.Bones do
  let node = findNode b.Name scene.RootNode
  let nodeT = if isNull node then "(no node)" else printM node.Transform
  printfn $"BONE {b.Name}"
  printfn $"  node-local: {nodeT}"
  printfn $"  offset    : {printM b.OffsetMatrix}"

printfn ""
printfn "=== CLIP 'idle' first pos-key vs node local translation ==="
let idle = scene.Animations |> Seq.find(fun a -> a.Name = "idle")

for ch in idle.NodeAnimationChannels do
  let node = findNode ch.NodeName scene.RootNode

  let nodeLocal =
    if isNull node then
      "(no node)"
    else
      $"({node.Transform.A4:g3},{node.Transform.B4:g3},{node.Transform.C4:g3})"

  let firstPos =
    if ch.HasPositionKeys && ch.PositionKeys.Count > 0 then
      let k = ch.PositionKeys[0]
      $"({k.Value.X:g3},{k.Value.Y:g3},{k.Value.Z:g3})"
    else
      "(none)"

  let firstScale =
    if ch.HasScalingKeys && ch.ScalingKeys.Count > 0 then
      let k = ch.ScalingKeys[0]
      $"({k.Value.X:g3},{k.Value.Y:g3},{k.Value.Z:g3})"
    else
      "(none)"

  printfn
    $"CH {ch.NodeName}: nodeLocal={nodeLocal} clipPos0={firstPos} clipScale0={firstScale}"

printfn ""
printfn "=== clip list ==="

for a in scene.Animations do
  printfn $"{a.Name} ch={a.NodeAnimationChannelCount}"
