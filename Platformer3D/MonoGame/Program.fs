module Platformer3D.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Input
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.MonoGame.Types
open Platformer3D.MonoGame.Systems

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

// The general-purpose clips (Idle_A among them) ship in a second rig file with
// the same skeleton — its animations are merged into the player clip set.
let private rawGeneralPath =
  System.IO.Path.Combine(
    AppContext.BaseDirectory,
    "animations",
    "Rig_Medium_General.glb"
  )

// Path to the raw kaykit weapon pack (fsproj <Content> entries, kept out of
// the .mgcb on purpose — same raw-load pattern as character-oobi.glb).
let private weaponsDir =
  System.IO.Path.Combine(AppContext.BaseDirectory, "weapons")

// Loads a raw .gltf weapon via AssimpNetter and uploads it as a PrimitiveMesh.
// Same post-process steps as Mibo.MonoGame's AssetsService.loadScene. Unlike
// content-pipeline ModelMeshParts the resulting vertices are in model-root
// space, so the attachment grip transform needs no bone-transform bake.
let private loadWeaponMesh (gd: GraphicsDevice) (path: string) : PrimitiveMesh =
  use importer = new Assimp.AssimpContext()

  let scene =
    importer.ImportFile(
      path,
      Assimp.PostProcessSteps.FindDegenerates
      ||| Assimp.PostProcessSteps.FindInvalidData
      ||| Assimp.PostProcessSteps.FlipUVs
      ||| Assimp.PostProcessSteps.FlipWindingOrder
      ||| Assimp.PostProcessSteps.JoinIdenticalVertices
      ||| Assimp.PostProcessSteps.ImproveCacheLocality
      ||| Assimp.PostProcessSteps.OptimizeMeshes
      ||| Assimp.PostProcessSteps.Triangulate
    )

  // Bake node transforms into the vertices: walk the node tree accumulating
  // world transforms. Assimp matrices are column-vector — transpose to
  // MonoGame's row-vector convention (same as Mibo.MonoGame/Animation3D.fs).
  let vertices = ResizeArray<VertexPositionNormalTexture>()
  let indices = ResizeArray<int>()

  let rec walk (node: Assimp.Node) (parentWorld: Matrix) =
    let world =
      Matrix.Transpose(Matrix.op_Implicit node.Transform) * parentWorld

    for meshIdx in node.MeshIndices do
      let mesh = scene.Meshes[meshIdx]
      let baseVertex = vertices.Count

      for i = 0 to mesh.VertexCount - 1 do
        let p = mesh.Vertices[i]
        let pos = Vector3.Transform(Vector3(p.X, p.Y, p.Z), world)
        let n = mesh.Normals[i]
        let normal = Vector3.TransformNormal(Vector3(n.X, n.Y, n.Z), world)
        let uv = mesh.TextureCoordinateChannels[0][i]

        vertices.Add(
          VertexPositionNormalTexture(pos, normal, Vector2(uv.X, uv.Y))
        )

      for idx in mesh.GetIndices() do
        indices.Add(baseVertex + idx)

    for child in node.Children do
      walk child world

  walk scene.RootNode Matrix.Identity

  let verts = vertices.ToArray()
  let idxs = indices.ToArray()

  let vb =
    new VertexBuffer(
      gd,
      typeof<VertexPositionNormalTexture>,
      verts.Length,
      BufferUsage.WriteOnly
    )

  vb.SetData(verts)

  let shortIndices = idxs |> Array.map int16

  let ib =
    new IndexBuffer(
      gd,
      IndexElementSize.SixteenBits,
      shortIndices.Length,
      BufferUsage.WriteOnly
    )

  ib.SetData(shortIndices)

  printfn $"[weapons] {path}: {verts.Length} verts, {idxs.Length / 3} tris"

  {
    Vertices = vb
    Indices = ib
    PrimitiveCount = idxs.Length / 3
    Bounds =
      BoundingSphere.CreateFromPoints(verts |> Seq.map(fun v -> v.Position))
  }

// Merge both KayKit rig files' animations into one clip set: the movement
// clips ship in Rig_Medium_MovementBasic.glb, the general-purpose clips
// (Idle_A among them) in Rig_Medium_General.glb — same skeleton. Clip names
// are the KayKit originals, which is what Platformer3D.Animation.targetClip
// returns.
let private buildPlayerClips(clips: Animation3DClip[]) : Animation3DClips =
  let info =
    Animation3DClipsInfo.create(
      clips |> Array.map(fun c -> c.Name, c.KeyframeCount)
    )

  {
    Clips = clips
    ClipNames = info.ClipNames
    ClipsInfo = info
  }

let loadInitialChunks(model: Model) =
  let spawnPos = spawnPosition

  let numericsSpawn =
    System.Numerics.Vector3(spawnPos.X, spawnPos.Y, spawnPos.Z)

  loadChunks numericsSpawn model.Chunks.Chunks model.Chunks.Seed

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key GameAction.MoveLeft KeyCode.A
    |> InputMap.key GameAction.MoveLeft KeyCode.Left
    |> InputMap.key GameAction.MoveRight KeyCode.D
    |> InputMap.key GameAction.MoveRight KeyCode.Right
    |> InputMap.key GameAction.MoveForward KeyCode.W
    |> InputMap.key GameAction.MoveForward KeyCode.Up
    |> InputMap.key GameAction.MoveBackward KeyCode.S
    |> InputMap.key GameAction.MoveBackward KeyCode.Down
    |> InputMap.key GameAction.Jump KeyCode.Space
    |> InputMap.key GameAction.Respawn KeyCode.R
    |> InputMap.key GameAction.RotateCameraLeft KeyCode.Q
    |> InputMap.key GameAction.RotateCameraRight KeyCode.E
    |> InputMap.key GameAction.RotateCameraUp KeyCode.PageUp
    |> InputMap.key GameAction.RotateCameraDown KeyCode.PageDown

  let model = Model()
  model.InputMap <- inputMap
  model.Chunks <- Chunks.init(Random.Shared.Next())
  loadInitialChunks model

  let gd = MonoGameGameContext.getGraphicsDevice ctx
  model.GraphicsDevice <- gd

  let particleTex = new Texture2D(gd, 1, 1, false, SurfaceFormat.Color)
  particleTex.SetData([| Color.White |])
  model.ParticleTexture <- particleTex

  let assets = GameContext.getService<IAssets> ctx
  model.JumpSound <- assets.Sound "sfx_jump"
  model.DiagFont <- assets.Font "diagnostics"

  // KayKit mannequin rig: the renderable Model comes from the .mgcb content
  // pipeline (SkinnedEffect), while the skeleton and animation clips are
  // raw-loaded from the same .glb via AssimpNetter (XNB drops animation data).
  // One rig file carries the meshes, the skeleton (including the
  // handslot.r/handslot.l attachment sockets), and the movement clips; the
  // general-purpose clips (Idle_A among them) ship in a second rig file with
  // the same skeleton — concatenate both into one clip set.
  let playerModel =
    assets.Model("kaykit_character_animations/Rig_Medium_MovementBasic")

  let animatedMesh = assets.AnimatedMesh rawModelPath

  let movementClips = assets.ModelAnimations rawModelPath
  let generalClips = assets.ModelAnimations rawGeneralPath

  let clips =
    buildPlayerClips(Array.append movementClips.Clips generalClips.Clips)

  printfn
    $"[player] rig: {playerModel.Meshes.Count} meshes, {clips.Clips.Length} clips ({movementClips.Clips.Length} movement + {generalClips.Clips.Length} general)"

  model.PlayerAnim <-
    AnimatedModel.create playerModel animatedMesh clips "Idle_A" 60.0f

  // Second playback state for the multi-pose demo: one Model + one
  // AnimatedMesh rendered twice per frame at different poses.
  model.PlayerAnim2 <-
    AnimatedModel.create playerModel animatedMesh clips "Walking_A" 60.0f

  match animatedMesh with
  | ValueSome animMesh ->
    let boneLabel name =
      match AnimatedMesh.tryFindBoneIndex name animMesh with
      | ValueSome i -> $"#{i}"
      | ValueNone -> "MISSING"

    let slotR = boneLabel "handslot.r"
    let slotL = boneLabel "handslot.l"

    printfn
      $"[player] skeleton: {animMesh.BoneCount} bones, handslot.r={slotR}, handslot.l={slotL}"
  | ValueNone -> printfn "[player] no animated mesh (rig has no skeleton?)"

  // The rig ships no texture — apply the shared mannequin albedo to every
  // SkinnedEffect (raw-loaded like the weapon texture below). DiffuseColor
  // must also be reset to white: the glb carries baseColorFactor 0.2, which
  // the content pipeline keeps as the diffuse tint and would darken the
  // texture to near-black.
  let mannequinTex =
    use stream =
      System.IO.File.OpenRead(
        System.IO.Path.Combine(
          AppContext.BaseDirectory,
          "animations",
          "mannequin_texture.png"
        )
      )

    Texture2D.FromStream(gd, stream)

  for mesh in playerModel.Meshes do
    for part in mesh.MeshParts do
      match part.Effect with
      | :? SkinnedEffect as skinned ->
        skinned.Texture <- mannequinTex
        skinned.DiffuseColor <- Vector3.One
      | _ ->
        printfn
          $"[player] WARNING: non-skinned effect on player mesh part: {part.Effect.GetType().Name}"

  // Bone-attachment demo: sword in the right hand, wand in the left, parented
  // to the handslot sockets at draw time. KayKit weapons are authored to snap
  // onto the handslots with the grip at the origin — identity local transform;
  // tune visually if a grip sits off.
  let weaponTex =
    use stream =
      System.IO.File.OpenRead(
        System.IO.Path.Combine(weaponsDir, "weapons_bits_texture.png")
      )

    Texture2D.FromStream(gd, stream)

  printfn $"[weapons] texture: {weaponTex.Width}x{weaponTex.Height}"

  let weaponMaterial =
    let mat = Material3D.defaults |> Material3D.withAlbedoMap weaponTex

    {
      mat with
          Roughness = 0.65f
          Metallic = 0.2f
    }

  let weaponSpecs = [|
    struct ("sword_A", "handslot.r", Matrix.Identity)
    struct ("wand_A", "handslot.l", Matrix.Identity)
  |]

  model.PlayerProps <- [|
    for struct (file, bone, local) in weaponSpecs do
      {
        BoneName = bone
        LocalTransform = local
        Mesh =
          loadWeaponMesh gd (System.IO.Path.Combine(weaponsDir, file + ".gltf"))
        Material = weaponMaterial
      }
  |]

  let target =
    spawnPosition + System.Numerics.Vector3(0.0f, playerHeight * 0.5f, 0.0f)

  model.Physics.CameraTarget <- target

  model.Physics.CameraPosition <-
    computeCameraPosition
      target
      model.Physics.CameraYaw
      model.Physics.CameraPitch

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

let overlayView (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  Platformer3D.MonoGame.MinimapView.view ctx model buffer
  Platformer3D.MonoGame.DiagnosticsView.view ctx model buffer

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo MonoGame 3D Platformer"
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = {
            ShadowBiasConfig.defaults with
                DirectionalBias = 0.002f
                SlopeScaleBias = 0.0008f
          },
          shadowAtlas = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
          }
        )

      Renderer3D.create pipeline Platformer3D.MonoGame.View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear overlayView)
    |> MonoGameProgram.ofProgram

  let game = new MiboGame<Model, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
