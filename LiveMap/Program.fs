module LiveMap.Program

open System
open System.IO
open System.Numerics
open LiveMap
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Input
open Mibo.Layout3D
open Raylib_cs

/// Everything a frame reads: the document that built, the camera, the
/// held input, the loaded assets, and what the pointer is over.
type Model = {
  Document: Document.Model
  Camera: Camera.Model
  Input: Input.Model
  Assets: Assets.Assets
  /// The instanced draw context for the block map: grouping and pooled
  /// transform arrays live here, so it is built once and reused.
  Blocks: InstancedRenderContext<BlockCell, string>
  Hover: Hover.Info voption
  Help: bool
  Viewport: Vector2
}

[<Struct>]
type Msg =
  /// One frame. Held keys move the camera, so the tick carries the frame
  /// time.
  | Tick of time: GameTime
  | InputChanged of state: Input.Msg
  | CameraChanged of camera: Camera.Msg
  | DocumentChanged of document: Document.Msg
  | Watched of watch: FileWatch.WatchEvent
  | ToggleHelp
  | Quit

// ── the document directory ───────────────────────────────────

/// Walks up from the executable to the folder that holds `LiveMap/maps`.
///
/// The point of the sample is editing a file while the game runs, so the
/// app watches the file a person edits in the checkout and never a copy
/// in its own output folder. Running from anywhere inside the repository
/// finds the same directory.
let private repositoryMaps(start: string) : string voption =
  let mutable directory = DirectoryInfo start
  let mutable found = ValueNone

  while found.IsNone && not(isNull directory) do
    let candidate = Path.Combine(directory.FullName, "LiveMap", "maps")

    if Directory.Exists candidate then
      found <- ValueSome candidate

    directory <- directory.Parent

  found

/// The maps directory: `--maps <dir>` when it is passed, the
/// repository's own folder otherwise.
let mapsDirectory(args: string[]) : string =
  let flag = args |> Array.tryFindIndex(fun arg -> arg = "--maps")

  match flag with
  | Some index when index + 1 < args.Length -> args[index + 1]
  | _ ->
    match repositoryMaps AppContext.BaseDirectory with
    | ValueSome directory -> directory
    | ValueNone -> Path.Combine(AppContext.BaseDirectory, "maps")

// ── the router ───────────────────────────────────────────────

/// What the pointer is over, resolved from the camera and the map.
///
/// The hover is display state, so it is recomputed every frame from the
/// mouse position rather than carried as a message.
let private resolveHover(model: Model) : Hover.Info voption =
  let pointer = Raylib.GetMousePosition()

  match model.Document.Map with
  | ValueNone -> ValueNone
  | ValueSome(Document.FlatMap layers) ->
    Hover.flat (Camera.flat model.Camera) pointer layers
  | ValueSome(Document.BlockMap(layers, _)) ->
    Hover.blocks (Camera.blocks model.Camera) pointer layers

/// Puts the map in the middle of the view.
let private reframe(model: Model) : Model =
  match model.Document.Map with
  | ValueNone -> model
  | ValueSome map ->
    // every layer spans the same map, so the map's size frames them all
    let camera =
      Camera.update
        (Camera.Frame(Document.Map.width map, Document.Map.height map))
        model.Camera

    { model with Camera = camera }

/// Switches the map. The document drops the old grid, so the next build
/// frames the view again.
let private selectMode (mode: Mode) (model: Model) : Model =
  if model.Document.Mode = mode then
    model
  else
    let struct (document, _) =
      Document.update (Document.SelectMode mode) model.Document

    {
      model with
          Document = document
          Hover = ValueNone
    }

/// Switches the document syntax. Both files hold the same map, so this is
/// also the parity check by eye.
let private selectSyntax(model: Model) : Model =
  let next =
    match model.Document.Syntax with
    | Kdl -> Xml
    | Xml -> Kdl

  let struct (document, _) =
    Document.update (Document.SelectSyntax next) model.Document

  {
    model with
        Document = document
        Hover = ValueNone
  }

/// Applies the one-shot keys of an input frame. Each is an edge, so a key
/// that is held acts once.
let private applyKeys
  (state: ActionState<Input.GameAction>)
  (model: Model)
  : Model * Cmd<Msg> =
  let model =
    if state.Started.Contains Input.FlatMode then
      selectMode Flat model
    else
      model

  let model =
    if state.Started.Contains Input.BlocksMode then
      selectMode Blocks model
    else
      model

  let model =
    if state.Started.Contains Input.SwapSyntax then
      selectSyntax model
    else
      model

  let model =
    if state.Started.Contains Input.ToggleHelp then
      { model with Help = not model.Help }
    else
      model

  let model =
    if state.Started.Contains Input.ResetView then
      reframe model
    else
      model

  let command =
    if state.Started.Contains Input.Quit then
      Cmd.ofMsg Quit
    else
      Cmd.none

  model, command

/// Applies one message. The router dispatches to a sub-system and
/// translates what comes back into commands; it holds no game logic.
let update
  (_ctx: GameContext)
  (msg: Msg)
  (model: Model)
  : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick time ->
    let dt = float32 time.ElapsedGameTime.TotalSeconds
    let intent = Input.intent model.Input

    let camera =
      Camera.update
        (Camera.Move(intent.Pan, intent.Zoom, intent.Rotate, dt))
        model.Camera

    struct ({
              model with
                  Camera = camera
                  Hover = resolveHover model
            },
            Cmd.none)

  | InputChanged inputMsg ->
    let struct (input, _) = Input.update inputMsg model.Input
    let model = { model with Input = input }

    match inputMsg with
    | Input.InputChanged state ->
      let model, command = applyKeys state model
      struct (model, command)

  | CameraChanged cameraMsg ->
    struct ({
              model with
                  Camera = Camera.update cameraMsg model.Camera
            },
            Cmd.none)

  | DocumentChanged documentMsg ->
    let struct (document, _) = Document.update documentMsg model.Document

    struct ({ model with Document = document }, Cmd.none)

  | Watched(FileWatch.Loaded source) ->
    let hadMap = model.Document.Map.IsSome

    let struct (document, _) =
      Document.update (Document.Loaded source) model.Document

    let model = { model with Document = document }

    // the first map of a mode frames the view; a later save must not move
    // the camera out from under whoever is editing
    if hadMap then
      struct (model, Cmd.none)
    else
      struct (reframe model, Cmd.none)

  | Watched(FileWatch.Failed reason) ->
    let struct (document, _) =
      Document.update (Document.ReadFailed reason) model.Document

    struct ({ model with Document = document }, Cmd.none)

  | ToggleHelp -> struct ({ model with Help = not model.Help }, Cmd.none)

  | Quit -> struct (model, Cmd.ofMsg Quit)

// ── subscriptions ────────────────────────────────────────────

/// The input map, and the watched document.
///
/// The watcher's subscription id carries the document path, so a mode or
/// syntax change makes the runtime dispose the old watcher and start the
/// new one. The new stream emits the file's current content at subscribe,
/// which is the whole reload path.
let subscribe (ctx: GameContext) (model: Model) : Sub<Msg> =
  let input =
    InputMapper.subscribeStatic
      Input.inputMap
      (Input.InputChanged >> InputChanged)
      ctx

  let watch =
    Sub.Active(
      SubId.ofString $"livemap/watch/{model.Document.Path}",
      fun dispatch ->
        FileWatch.watch model.Document.Path
        |> Observable.subscribe(Watched >> dispatch)
    )

  Sub.batch [ input; watch ]

// ── views ────────────────────────────────────────────────────

/// The 3D pass. It draws nothing unless a block map is loaded, which
/// leaves the window to the 2D pass in the flat mode.
let private viewBlocks
  (_ctx: GameContext)
  (model: Model)
  (buffer: RenderBuffer3D)
  =
  match model.Document.Map with
  | ValueSome(Document.BlockMap(layers, drawn)) ->
    BlocksView.view
      model.Blocks
      (Camera.blocks model.Camera)
      drawn
      layers
      model.Hover
      buffer
  | _ -> ()

/// The 2D pass: the flat map, then the overlay.
let private viewOverlay
  (_ctx: GameContext)
  (model: Model)
  (buffer: RenderBuffer2D)
  =
  match model.Document.Map with
  | ValueSome(Document.FlatMap layers) ->
    MapView.view
      model.Assets
      (Camera.flat model.Camera)
      model.Viewport
      layers
      model.Hover
      buffer
  | _ -> ()

  Hud.view
    model.Assets
    model.Document
    model.Hover
    model.Help
    model.Viewport
    buffer

// ── boot ─────────────────────────────────────────────────────

let init(ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let assets = Assets.load ctx
  let viewport = Vector2(float32 ctx.WindowWidth, float32 ctx.WindowHeight)
  let directory = mapsDirectory(Environment.GetCommandLineArgs())

  let document = Document.init directory

  // read the document once here so the first frame shows the map; the
  // watcher's own first read arrives right after and rebuilds the same
  // grid
  let struct (document, _) =
    if File.Exists document.Path then
      Document.update (Document.Loaded(File.ReadAllText document.Path)) document
    else
      struct ({
                document with
                    Status = $"no document at {document.Path}"
              },
              ValueNone)

  let model = {
    Document = document
    Camera = Camera.init viewport
    Input = Input.init
    Assets = assets
    Blocks = BlocksView.contextFor assets
    Hover = ValueNone
    Help = true
    Viewport = viewport
  }

  struct (reframe model, Cmd.none)

[<EntryPoint>]
let main args =
  // `--check` builds every document, prints the result, and exits: a
  // broken map is caught without a window
  if args |> Array.contains "--check" then
    Check.run(mapsDirectory args)
  else
    let program =
      Program.mkProgramCtx init update
      |> Program.withAssetsBasePath AppContext.BaseDirectory
      |> Program.withConfig(fun config -> {
        config with
            Width = Constants.windowWidth
            Height = Constants.windowHeight
            Title = "Mibo LiveMap"
            // the editor draws a mostly static grid: cap the loop so the
            // GPU is not asked for frames nobody sees
            TargetFPS = ValueSome 60
      })
      |> Program.withInput
      |> Program.withSubscription subscribe
      |> Program.withTick Tick
      |> Program.withRenderer(fun () ->
        Renderer3D.create (ForwardPbrPipeline()) viewBlocks)
      |> Program.withRenderer(fun () ->
        Renderer2D.createWith Renderer2DConfig.noClear viewOverlay)

    let game = new RaylibGame<Model, Msg>(program)
    game.Run()
    0
