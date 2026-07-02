module BoneProbe.Palette

open System
open Microsoft.Xna.Framework
open Mibo.Animation
open BoneProbe.Scene

let private identityError(m: Matrix) =
  let maxOffDiag =
    [|
      m.M12
      m.M13
      m.M14
      m.M21
      m.M23
      m.M24
      m.M31
      m.M32
      m.M34
      m.M41
      m.M42
      m.M43
    |]
    |> Array.map abs
    |> Array.max

  let maxDiag =
    [|
      abs(m.M11 - 1.0f)
      abs(m.M22 - 1.0f)
      abs(m.M33 - 1.0f)
      abs(m.M44 - 1.0f)
    |]
    |> Array.max

  max maxOffDiag maxDiag

let private isIdentity(m: Matrix) = identityError m < 1e-3f

let private matrixCompact(m: Matrix) =
  $"[{m.M11:g4},{m.M12:g4},{m.M13:g4},{m.M14:g4},{m.M21:g4},{m.M22:g4},{m.M23:g4},{m.M24:g4},{m.M31:g4},{m.M32:g4},{m.M33:g4},{m.M34:g4},{m.M41:g4},{m.M42:g4},{m.M43:g4},{m.M44:g4}]"

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
    let meshResult = AnimatedMesh.fromScene scene

    match meshResult with
    | ValueNone ->
      eprintfn $"No skeleton found in model: {path}"
      1
    | ValueSome mesh ->
      let clips = Animation3DClips.fromScene scene

      let verb = options.Verbosity
      let focus = options.Focus

      let focusStr =
        match focus with
        | None -> "-"
        | Some s -> s

      printfn
        $"palette path={path} bones={mesh.BoneCount} clips={Animation3DClips.count clips} verbosity={verb:A} focus={focusStr}"

      if verb = Full then
        let clipNames = Animation3DClips.names clips

        for i = 0 to (Animation3DClips.count clips) - 1 do
          let clipName = clipNames[i]
          let clip = clips.Clips[i]

          printfn
            $"c{i} {clipName} ch={clip.Channels.Count} kf={clip.KeyframeCount}"

      if verb = Full then
        printfn "bindpose"

      let boneCount = mesh.BoneCount
      let parents = mesh.BoneParents
      let bindLocal = mesh.BindLocalPoses
      let invBind = mesh.InverseBindPose
      let order = mesh.BoneOrder

      let worldBind = Array.zeroCreate<Matrix> boneCount

      for i in order do
        let p = parents[i]

        worldBind[i] <-
          if p < 0 then bindLocal[i] else bindLocal[i] * worldBind[p]

        let palette = invBind[i] * worldBind[i]
        let name = mesh.BoneNames[i]

        if matchesFocus focus name then
          if isIdentity palette then
            if verb = Full then
              printfn $"b{i} {name} p={p} PASS"
          else
            let err = identityError palette

            if verb = Full then
              let matStr =
                if verb = Full then $" m={matrixCompact palette}" else ""

              printfn
                $"b{i} {name} p={p} FAIL err={err:g3}{matStr} t=({palette.M41:g3},{palette.M42:g3},{palette.M43:g3})"

      let totalPass =
        Array.fold
          (fun acc i ->
            if isIdentity(invBind[i] * worldBind[i]) then
              acc + 1
            else
              acc)
          0
          order

      let totalFail = boneCount - totalPass

      printfn $"summary bones={boneCount} pass={totalPass} fail={totalFail}"

      if totalFail > 0 then 1 else 0
