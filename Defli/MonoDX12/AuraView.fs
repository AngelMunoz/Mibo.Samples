namespace Defli.MonoGame

open System
open System.Numerics
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Defli.State
open Defli.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// AuraView — the MonoGame twin of the raylib AuraView. One HLSL
// effect (content pipeline: Shaders/Aura), loaded lazily on
// first draw.
//
// Each aura is ONE self-contained DrawImmediate command — the
// documented escape hatch (docs/graphics2d/custom-commands.md):
// the renderer flushes its batches and exits camera/shader modes,
// the action runs raw device calls, then the previous modes are
// restored. The action sets the effect's MatrixTransform (the
// documented 2D view-projection contract, docs/shader-uniforms.md)
// to the world pass's camera × orthographic projection, uploads
// the per-aura uniforms, and draws the disc with
// DrawUserPrimitives. Per-aura uniforms stay paired with their
// disc because everything happens inside a single command — a
// beginShader/endShader scope cannot carry per-draw uniforms
// through the deferred stream.
//
// Geometry: VertexPositionTexture through a DynamicVertexBuffer —
// the main Mibo shader contract (ForwardPbr.fx): no vertex colors
// (the color rides the AuraColor uniform), POSITION0 + TEXCOORD0.
// The circle's bbox-mapped UVs carry the radial coordinate for the
// pixel shader, and AuraMask is a radial falloff baked once at
// boot.
// Fallback: the old circle outline when the effect is unavailable.
// ─────────────────────────────────────────────────────────────

[<Sealed>]
type AuraView() =

  let mutable effect = ValueNone
  let mutable mask = ValueNone
  let mutable attempted = false

  // Triangle-list scratch, grown once and reused (same pattern the
  // VfxView uses for its particle conversion buffers).
  let mutable fan = Array.empty<VertexPositionTexture>

  // The uploaded geometry — a DynamicVertexBuffer, grown once and
  // reused (SetData + Discard per draw, like the framework batches).
  let mutable vertexBuffer = ValueNone

  /// Radial falloff mask (white center → transparent rim), sampled by
  /// the pixel shader as the aura's soft wash. Baked once at boot on
  /// the first draw (the device exists by then).
  let createMask(gd: GraphicsDevice) : Texture2D =
    let size = 128
    let data = Array.zeroCreate<Microsoft.Xna.Framework.Color>(size * size)

    for y in 0 .. size - 1 do
      for x in 0 .. size - 1 do
        let dx = (float32 x + 0.5f) / float32 size - 0.5f
        let dy = (float32 y + 0.5f) / float32 size - 0.5f
        let t = sqrt(dx * dx + dy * dy) * 2f

        // CPU smoothstep: 1 at the center, 0 at the rim.
        let u = min 1f (max 0f t)
        let falloff = 1f - (u * u * (3f - 2f * u))
        let a = byte(falloff * 255f)

        data.[y * size + x] <-
          Microsoft.Xna.Framework.Color(255uy, 255uy, 255uy, a)

    let tex = new Texture2D(gd, size, size)
    tex.SetData(data)
    tex

  let ensureEffect(ctx: GameContext) =
    if not attempted then
      attempted <- true

      try
        let assets = GameContext.getService<IAssets> ctx
        let e = assets.Effect Paths.Aura
        e.CurrentTechnique <- e.Techniques.[0]
        effect <- ValueSome e
        mask <- ValueSome(createMask(MonoGameGameContext.getGraphicsDevice ctx))
      with _ ->
        ()

  /// Draws one shader aura: a soft radial glow with a bright band at
  /// `ringPos` (0..1 of the radius), additively blended. `time` is the
  /// frame's total seconds (drives the pulse/shimmer). `camera` is the
  /// frame's neutral camera snapshot and `viewport` the window size —
  /// the action rebuilds the exact camera the world pass drew with.
  member _.Draw
    (ctx: GameContext)
    (camera: CameraState)
    (viewport: Vector2)
    (center: Vector2)
    (radius: float32)
    (color: Mibo.Color)
    (ringPos: float32)
    (time: float32)
    (buffer: RenderBuffer2D)
    =
    let gd = MonoGameGameContext.getGraphicsDevice ctx
    ensureEffect ctx

    match effect, mask with
    | ValueNone, _
    | _, ValueNone ->
      buffer.circleOutline(center, radius, color, layer = Layers.Effects).drop()

    | ValueSome e, ValueSome maskTex ->
      let rgba = Color.toVector4 color

      buffer
        .drawImmediate(
          (fun () ->

            let world = Camera2D.toMatrix(CameraView.toMono camera viewport)

            let w = float32 gd.Viewport.Width
            let h = float32 gd.Viewport.Height

            let proj =
              Microsoft.Xna.Framework.Matrix.CreateOrthographicOffCenter(
                0f,
                w,
                h,
                0f,
                0f,
                -1f
              )

            // The documented 2D view-projection contract.
            e.Parameters.["MatrixTransform"].SetValue(world * proj)
            e.Parameters.["AuraTime"].SetValue(time)
            e.Parameters.["AuraColor"].SetValue(Xna.v4 rgba)
            e.Parameters.["AuraRing"].SetValue(ringPos)
            e.Parameters.["AuraMask"].SetValue(maskTex)

            // Disc tessellation (same segment count the renderer's
            // fillCircle uses), written as a triangle list. UVs are
            // bbox-mapped around (0.5, 0.5): 0 at the center, 1 at
            // the rim — the radial coordinate the pixel shader uses.
            let segments = max 3 (int(radius / 2f) + 8)

            let vertexCount = segments * 3

            if fan.Length < vertexCount then
              fan <- Array.zeroCreate<VertexPositionTexture> vertexCount

            let centerV =
              Microsoft.Xna.Framework.Vector3(center.X, center.Y, 0f)

            let centerUv = Microsoft.Xna.Framework.Vector2(0.5f, 0.5f)
            let step = MathF.PI * 2f / float32 segments

            for i in 0 .. segments - 1 do
              let a = float32 i * step
              let b = float32(i + 1) * step

              let rimV angle =
                Microsoft.Xna.Framework.Vector3(
                  center.X + MathF.Cos angle * radius,
                  center.Y + MathF.Sin angle * radius,
                  0f
                )

              let rimUv angle =
                Microsoft.Xna.Framework.Vector2(
                  0.5f + MathF.Cos angle * 0.5f,
                  0.5f + MathF.Sin angle * 0.5f
                )

              fan.[i * 3] <- VertexPositionTexture(centerV, centerUv)
              fan.[i * 3 + 1] <- VertexPositionTexture(rimV a, rimUv a)
              fan.[i * 3 + 2] <- VertexPositionTexture(rimV b, rimUv b)

            // Upload through a dynamic vertex buffer (grown once) and
            // draw with SetVertexBuffer + DrawPrimitives — the same
            // device path every framework draw uses, never
            // DrawUserPrimitives.
            let vb =
              match vertexBuffer with
              | ValueSome(b: DynamicVertexBuffer) when
                b.VertexCount >= vertexCount
                ->
                b
              | _ ->
                let b =
                  new DynamicVertexBuffer(
                    gd,
                    VertexPositionTexture.VertexDeclaration,
                    vertexCount,
                    BufferUsage.WriteOnly
                  )

                vertexBuffer <- ValueSome b
                b

            vb.SetData(fan, 0, vertexCount, SetDataOptions.Discard)
            gd.SetVertexBuffer(vb)

            let prevBlend = gd.BlendState
            let prevDepth = gd.DepthStencilState
            gd.BlendState <- BlendState.Additive
            gd.DepthStencilState <- DepthStencilState.None
            e.CurrentTechnique.Passes.[0].Apply()
            gd.DrawPrimitives(PrimitiveType.TriangleList, 0, segments)
            gd.BlendState <- prevBlend
            gd.DepthStencilState <- prevDepth),
          layer = Layers.Effects
        )
        .drop()
