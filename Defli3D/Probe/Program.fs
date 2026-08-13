module Defli3D.Probe.Program

open System
open System.Collections.Generic
open System.IO
open System.Numerics
open System.Reflection
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

// ─────────────────────────────────────────────────────────────
// Scratch diagnostic: which 3D draw paths actually render on the
// MonoGame DX12 runtime? A real Game host (the same device-creation
// path the samples use — a bare `new GraphicsDevice` fails swapchain
// creation on the native DX12 runtime), rendering five probes
// through the real ForwardPipeline into an offscreen render target,
// then reporting pixel counts per hue:
//
//   ground             opaque white mesh (PBR Standard)  — control
//   red line cross     Draw3D.line3D (LineList)          — the range ring path
//   green quad         instanced + per-instance color    — the preview/aura path
//   blue quad          instanced, no colors              — control
//   yellow quad        mesh, Material3D.Opacity = 0.6    — the fallback path
//
// A probe that renders nothing (count ≈ 0) is broken on this
// backend. Run: dotnet run --project Defli3D/Probe
// ─────────────────────────────────────────────────────────────

type ProbeGame() as this =
  inherit Game()

  do
    let gdm = new GraphicsDeviceManager(this)
    gdm.GraphicsProfile <- GraphicsProfile.HiDef
    gdm.IsFullScreen <- false
    gdm.PreferredBackBufferWidth <- 640
    gdm.PreferredBackBufferHeight <- 480
    gdm.SynchronizeWithVerticalRetrace <- false
    this.IsFixedTimeStep <- false
    this.Window.Title <- "Defli3D render probe"

  override this.Draw(_gameTime) =
    try
      let gd = this.GraphicsDevice
      let width = gd.PresentationParameters.BackBufferWidth
      let height = gd.PresentationParameters.BackBufferHeight

      // GameContext has an internal ctor + service registry; the
      // pipeline reads the GraphicsDevice service from it.
      let ctx =
        Activator.CreateInstance(
          typeof<GameContext>,
          BindingFlags.Instance ||| BindingFlags.NonPublic,
          null,
          [| box width; box height |],
          null
        )
        :?> GameContext

      let services =
        ctx
          .GetType()
          .GetProperty(
            "Services",
            BindingFlags.Instance ||| BindingFlags.NonPublic
          )
          .GetValue(ctx)
        :?> Dictionary<Type, obj>

      services[typeof<GraphicsDevice>] <- box gd

      // A flat unit quad (two triangles) as the probe geometry.
      let quadVerts = [|
        VertexPositionNormalTexture(
          Vector3(-0.5f, 0f, -0.5f),
          Vector3.UnitY,
          Vector2.Zero
        )
        VertexPositionNormalTexture(
          Vector3(0.5f, 0f, -0.5f),
          Vector3.UnitY,
          Vector2.Zero
        )
        VertexPositionNormalTexture(
          Vector3(0.5f, 0f, 0.5f),
          Vector3.UnitY,
          Vector2.Zero
        )
        VertexPositionNormalTexture(
          Vector3(-0.5f, 0f, 0.5f),
          Vector3.UnitY,
          Vector2.Zero
        )
      |]

      let quadIndices = [| 0; 1; 2; 0; 2; 3 |]

      let vb =
        new VertexBuffer(
          gd,
          VertexPositionNormalTexture.VertexDeclaration,
          4,
          BufferUsage.WriteOnly
        )

      vb.SetData(quadVerts)

      let ib =
        new IndexBuffer(
          gd,
          IndexElementSize.ThirtyTwoBits,
          6,
          BufferUsage.WriteOnly
        )

      ib.SetData(quadIndices)

      let quad = {
        Vertices = vb
        Indices = ib
        PrimitiveCount = 2
        Bounds = BoundingSphere(Vector3.Zero, 1.0f)
      }

      // Fill the render buffer: camera (45° FOV, eye (0,6,6) → origin),
      // ambient-only lighting (no shadow pass), then the probes.
      let buffer = new RenderBuffer3D(capacity = 4096)

      let camera =
        Camera3D.create (Vector3(0f, 6f, 6f)) Vector3.Zero (MathF.PI / 4f)

      buffer
        .beginCameraWith(
          Camera3D.render camera |> Camera3D.withClear(Color(46, 58, 72))
        )
        .setAmbientLight(
          {
            Color = Mibo.Color.White
            Intensity = 1.0f
          }
        )
        .drop()

      let opaqueWhite = Material3D.defaults

      // Ground (opaque mesh control) — DARK so line/quad colors stand out.
      buffer.mesh(
        quad,
        Matrix.CreateTranslation(0f, 0f, 0f),
        {
          opaqueWhite with
              AlbedoColor = Color(40, 40, 40)
        }
      )
      |> ignore

      // Red line cross at y = 0.5 (the range-ring draw path).
      buffer
        .line3D(
          System.Numerics.Vector3(-0.7f, 0.5f, 0f),
          System.Numerics.Vector3(0.7f, 0.5f, 0f),
          Mibo.Color.Red
        )
        .line3D(
          System.Numerics.Vector3(0f, 0.5f, -0.7f),
          System.Numerics.Vector3(0f, 0.5f, 0.7f),
          Mibo.Color.Red
        )
        .drop()

      // Tinted instanced quad (green, alpha 128 — the placement-preview /
      // boss-aura path: per-instance color on TEXCOORD5).
      buffer.instanced(
        quad,
        [| Matrix.CreateTranslation(0.9f, 0.02f, 0.9f) |],
        opaqueWhite,
        1,
        colors = [| Color(0, 255, 0, 128) |]
      )
      |> ignore

      // Plain instanced quad (BLUE material — control: towers render via
      // this; the material albedo should carry the color).
      buffer.instanced(
        quad,
        [| Matrix.CreateTranslation(-0.9f, 0.02f, 0.9f) |],
        Material3D.colored(Color.Blue),
        1
      )
      |> ignore

      // Translucent mesh quad (yellow, opacity 0.6 — the proposed
      // fallback path: Material3D.Opacity routes through the sorted
      // translucent pass).
      buffer.mesh(
        quad,
        Matrix.CreateTranslation(-0.9f, 0.02f, -0.9f),
        {
          Material3D.colored(Color(255, 220, 0)) with
              Opacity = 0.6f
        }
      )
      |> ignore

      buffer.endCamera() |> ignore

      // Execute the real pipeline into an offscreen RT.
      let rt =
        new RenderTarget2D(
          gd,
          width,
          height,
          false,
          SurfaceFormat.Color,
          DepthFormat.Depth24
        )

      gd.SetRenderTarget(rt)

      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = ShadowAtlasConfig.defaults
        )

      let rtPool = new RenderTargetPool3D(gd)

      (pipeline :> IRenderPipeline3D)
        .Execute(
          ctx,
          {
            Mibo.Elmish.GameTime.TotalTime = _gameTime.TotalGameTime
            ElapsedGameTime = _gameTime.ElapsedGameTime
          },
          buffer,
          rtPool
        )

      gd.SetRenderTarget(null)

      // Read + classify pixels.
      let pixels = Array.zeroCreate<Color>(width * height)
      rt.GetData(pixels)

      let mutable redN = 0
      let mutable greenN = 0
      let mutable blueN = 0
      let mutable yellowN = 0
      let mutable whiteN = 0
      let mutable otherN = 0

      let mutable redMinX = Int32.MaxValue
      let mutable redMaxX = Int32.MinValue
      let mutable redMinY = Int32.MaxValue
      let mutable redMaxY = Int32.MinValue

      let mutable greenMinX = Int32.MaxValue
      let mutable greenMaxX = Int32.MinValue
      let mutable greenMinY = Int32.MaxValue
      let mutable greenMaxY = Int32.MinValue

      for i = 0 to pixels.Length - 1 do
        let c = pixels[i]
        let r = int c.R
        let g = int c.G
        let b = int c.B
        let x = i % width
        let y = i / width

        let isRed = r > 90 && r > g * 3 / 2 && r > b * 3 / 2

        let isGreen = g > 90 && g > r * 3 / 2 && g > b * 3 / 2

        let isBlue = b > 90 && b > r * 3 / 2 && b > g * 3 / 2

        let isYellow = r > 100 && g > 100 && b < r * 4 / 5 && b < g * 4 / 5

        let isWhite = r > 200 && g > 200 && b > 200

        // The red cross should occupy a horizontal band around screen
        // y ≈ 200-260 and a vertical band around x ≈ 320.
        if isRed then
          redN <- redN + 1
          redMinX <- min redMinX x
          redMaxX <- max redMaxX x
          redMinY <- min redMinY y
          redMaxY <- max redMaxY y
        elif isGreen then
          greenN <- greenN + 1
          greenMinX <- min greenMinX x
          greenMaxX <- max greenMaxX x
          greenMinY <- min greenMinY y
          greenMaxY <- max greenMaxY y
        elif isBlue then
          blueN <- blueN + 1
        elif isYellow then
          yellowN <- yellowN + 1
        elif isWhite then
          whiteN <- whiteN + 1
        else
          otherN <- otherN + 1

      printfn
        "Probe: %dx%d, camera (0,6,6)->origin FOV45, ambient-only"
        width
        height

      printfn "  ground (dark mesh)        : %d px" whiteN

      printfn
        "  line cross (RED line3D)   : %d px  bbox x[%d..%d] y[%d..%d]"
        redN
        redMinX
        redMaxX
        redMinY
        redMaxY

      printfn
        "  tinted instanced (GREEN)  : %d px  bbox x[%d..%d] y[%d..%d]"
        greenN
        greenMinX
        greenMaxX
        greenMinY
        greenMaxY

      printfn "  plain instanced (BLUE)    : %d px" blueN
      printfn "  translucent mesh (YELLOW) : %d px" yellowN
      printfn "  other                     : %d px" otherN

      // Vertical strip through the line cross (x = 320, y = 180..320).
      printfn "  strip x=320:"

      for y in [ 180..10..320 ] do
        let c = pixels[y * width + 320]
        printfn "    y=%d (%d,%d,%d)" y (int c.R) (int c.G) (int c.B)

      let png = Path.Combine(AppContext.BaseDirectory, "probe.png")

      use fs = File.Open(png, FileMode.Create)
      rt.SaveAsPng(fs, width, height)
      printfn "saved %s" png
    with ex ->
      printfn "PROBE FAILED: %s" (ex.ToString())

    this.Exit()

[<EntryPoint>]
let main _ =
  use game = new ProbeGame()
  game.Run()
  0
