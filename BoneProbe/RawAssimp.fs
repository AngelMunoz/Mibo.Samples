module BoneProbe.RawAssimp

open System
open Assimp
open BoneProbe.Scene

let private matchesFocus (focus: string option) (name: string) =
  match focus with
  | None -> true
  | Some f -> name.Contains(f)

let probe(options: Options) =
  let path = options.Path

  match tryLoad path with
  | ValueNone ->
    eprintfn $"Failed to read model: {path}"

    eprintfn
      $"Usage: dotnet run --project BoneProbe -- [raw|palette] <path-to-glb> [-v full|summary] [-f <name>]"

    1
  | ValueSome scene ->
    printfn
      $"scene path={path} meshes={scene.MeshCount} anims={scene.AnimationCount} mats={scene.MaterialCount} tex={scene.TextureCount} root={scene.RootNode.Name}"

    let opts = options.Verbosity
    let focus = options.Focus

    if opts = Full then
      printfn "nodes"

      let rec printNode (indent: string) (n: Node) =
        let meshInfo =
          if n.MeshCount > 0 then
            let meshIndices =
              [ for i in 0 .. n.MeshCount - 1 -> string n.MeshIndices[i] ]
              |> String.concat ","

            $" m=[{meshIndices}]"
          else
            ""

        printfn $"{indent}- {n.Name} c={n.ChildCount}{meshInfo}"

        let next = indent + "  "

        for i in 0 .. n.ChildCount - 1 do
          printNode next n.Children[i]

      printNode "" scene.RootNode

    printfn "meshes"

    for mi = 0 to scene.MeshCount - 1 do
      let m = scene.Meshes[mi]

      if opts = Full then
        printfn
          $"m{mi} name={m.Name} verts={m.VertexCount} faces={m.FaceCount} bones={m.BoneCount} mat={m.MaterialIndex}"

        if m.HasBones then
          for bi = 0 to m.BoneCount - 1 do
            let b = m.Bones[bi]

            let showWeight =
              if matchesFocus focus b.Name then
                let firstWeights =
                  b.VertexWeights
                  |> Seq.take(min 2 b.VertexWeightCount)
                  |> Seq.map(fun w -> $"v{w.VertexID}={w.Weight:g2}")
                  |> String.concat " "

                $" {firstWeights}"
              else
                ""

            printfn $"  b{bi} {b.Name} w={b.VertexWeightCount}{showWeight}"
      else
        printfn
          $"m{mi} name={m.Name} verts={m.VertexCount} faces={m.FaceCount} bones={m.BoneCount}"

    if opts = Full then
      printfn "anims"

      if scene.HasAnimations then
        for ai = 0 to scene.AnimationCount - 1 do
          let anim = scene.Animations[ai]

          let tps =
            if anim.TicksPerSecond > 0.0 then
              anim.TicksPerSecond
            else
              25.0

          let durSec = anim.DurationInTicks / tps

          printfn
            $"a{ai} {anim.Name} dur={durSec:f2}s tps={tps:f0} ch={anim.NodeAnimationChannelCount}"

          for ci = 0 to anim.NodeAnimationChannelCount - 1 do
            let ch = anim.NodeAnimationChannels[ci]

            if matchesFocus focus ch.NodeName then
              let posCount =
                if ch.HasPositionKeys then ch.PositionKeys.Count else 0

              let rotCount =
                if ch.HasRotationKeys then ch.RotationKeys.Count else 0

              let scaleCount =
                if ch.HasScalingKeys then ch.ScalingKeys.Count else 0

              printfn
                $"  ch {ch.NodeName} p={posCount} r={rotCount} s={scaleCount}"
      else
        printfn "(none)"

    0
