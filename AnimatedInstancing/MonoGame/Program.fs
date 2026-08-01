module AnimatedInstancing.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Input
open AnimatedInstancing
open AnimatedInstancing.MonoGame.Types
open AnimatedInstancing.MonoGame.Systems

// Paths to the raw KayKit rig .glbs (copied to the output dir via the fsproj
// <Content> entries). The content pipeline does not preserve animation data
// in XNB, so clips + skeleton are raw-loaded via AssimpNetter at runtime —
// while the renderable Model comes from the .mgcb (SkinnedEffect).
let private rawModelPath =
  System.IO.Path.Combine(
    AppContext.BaseDirectory,
    "animations",
    "Rig_Medium_MovementBasic.glb"
  )

// The general-purpose clips (Idle_A among them) ship in a second rig file
// with the same skeleton — concatenate both into one clip set.
let private rawGeneralPath =
  System.IO.Path.Combine(
    AppContext.BaseDirectory,
    "animations",
    "Rig_Medium_General.glb"
  )


let private buildClips(clips: Animation3DClip[]) : Animation3DClips =
  let info =
    Animation3DClipsInfo.create(
      clips |> Array.map(fun c -> c.Name, c.KeyframeCount)
    )

  {
    Clips = clips
    ClipNames = info.ClipNames
    ClipsInfo = info
  }

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key Tier1 KeyCode.D1
    |> InputMap.key Tier2 KeyCode.D2
    |> InputMap.key Tier3 KeyCode.D3
    |> InputMap.key Tier4 KeyCode.D4
    |> InputMap.key TierUp KeyCode.Equal
    |> InputMap.key TierDown KeyCode.Minus
    |> InputMap.key TogglePause KeyCode.Space
    |> InputMap.key ToggleShadows KeyCode.S

  let model = Model()
  model.InputMap <- inputMap

  let gd = MonoGameGameContext.getGraphicsDevice ctx
  let assets = GameContext.getService<IAssets> ctx
  model.DiagFont <- assets.Font "diagnostics"

  // KayKit mannequin: the renderable Model comes from the .mgcb content
  // pipeline (Rig_Medium_MovementBasic.glb, SkinnedEffect, texture
  // embedded), while the skeleton and animation clips are raw-loaded from
  // the rig .glbs via AssimpNetter (XNB drops animation data).
  let rigModel =
    assets.Model "kaykit_character_animations/Rig_Medium_MovementBasic"

  let animatedMesh = assets.AnimatedMesh rawModelPath
  let movementClips = assets.ModelAnimations rawModelPath
  let generalClips = assets.ModelAnimations rawGeneralPath

  let clips = buildClips(Array.append movementClips.Clips generalClips.Clips)

  printfn
    $"[crowd] rig: {rigModel.Meshes.Count} meshes, {clips.Clips.Length} clips ({movementClips.Clips.Length} movement + {generalClips.Clips.Length} general)"

  match animatedMesh with
  | ValueSome animMesh ->
    printfn $"[crowd] skeleton: {animMesh.BoneCount} bones"
  | ValueNone -> printfn "[crowd] no animated mesh (rig has no skeleton?)"

  // Sanity check: every part must carry a SkinnedEffect (the pipeline's
  // DefaultEffect=SkinnedEffect) — the skinned-instanced draw keys off it.
  for mesh in rigModel.Meshes do
    for part in mesh.MeshParts do
      match part.Effect with
      | :? SkinnedEffect -> ()
      | _ ->
        printfn
          $"[crowd] WARNING: non-skinned effect on rig mesh part: {part.Effect.GetType().Name}"

  model.Rig <- {
    Model = rigModel
    Mesh = animatedMesh
    Clips = clips
  }

  // Ground slab: unit cube primitive, scaled to the grid at draw time.
  let primitives = Primitive3D.create gd
  model.GroundMesh <- primitives.Cylinder

  // Start at tier 1 (500 instances).
  model.Crowd <- Crowd.init model.Rig 0

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo Animated Instancing (MonoGame)"
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = ShadowAtlasConfig.defaults
        )

      Renderer3D.create pipeline View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear View.viewHud)
    |> MonoGameProgram.ofProgram

  let game = new MiboGame<Model, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
