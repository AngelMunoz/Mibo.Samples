namespace FPSSample.MonoShared

open System
open System.Diagnostics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open FPSSample

/// Procedural starry skybox for MonoGame. Renders a sphere from the inside
/// with a custom HLSL effect that generates multi-layer twinkling stars +
/// nebula clouds. Uses Draw3D.drawImmediate for custom render states
/// (no backface culling, no depth write).
module Skybox =

  type private SkyboxState = {
    Effect: Effect
    SphereVerts: VertexBuffer
    SphereIndices: IndexBuffer
    SpherePrimCount: int
    NoCullNoDepth: RasterizerState
    DepthOff: DepthStencilState
  }

  let mutable private state: SkyboxState voption = ValueNone

  /// Builds a UV sphere as VertexPositionNormalTexture data.
  let private buildSphereData(rings: int, segments: int) =
    let vertexCount = (rings + 1) * (segments + 1)
    let verts = Array.zeroCreate<VertexPositionNormalTexture> vertexCount
    let mutable vi = 0

    for ring = 0 to rings do
      let v0 = float32 ring / float32 rings
      let phi = v0 * float32 Math.PI
      let sinPhi = MathF.Sin(phi)
      let cosPhi = MathF.Cos(phi)

      for seg = 0 to segments do
        let u0 = float32 seg / float32 segments
        let theta = u0 * 2.0f * float32 Math.PI
        let x = sinPhi * MathF.Sin(theta)
        let y = cosPhi
        let z = sinPhi * MathF.Cos(theta)

        verts[vi] <-
          VertexPositionNormalTexture(
            Vector3(x, y, z),
            Vector3(x, y, z),
            Vector2(u0, v0)
          )

        vi <- vi + 1

    let indexCount = rings * segments * 6
    let indices = Array.zeroCreate<int> indexCount
    let mutable ii = 0

    for ring = 0 to rings - 1 do
      for seg = 0 to segments - 1 do
        let a = ring * (segments + 1) + seg
        let b = a + 1
        let c = a + (segments + 1)
        let d = c + 1
        indices[ii + 0] <- a
        indices[ii + 1] <- b
        indices[ii + 2] <- c
        indices[ii + 3] <- c
        indices[ii + 4] <- b
        indices[ii + 5] <- d
        ii <- ii + 6

    let shortIndices = indices |> Array.map int16
    struct (verts, shortIndices)

  let private ensureState (assets: IAssets) (gd: GraphicsDevice) =
    match state with
    | ValueSome s -> s
    | ValueNone ->
      let effect = assets.Effect("Skybox")
      let struct (verts, indices) = buildSphereData(24, 24)

      let vb =
        new VertexBuffer(
          gd,
          typeof<VertexPositionNormalTexture>,
          verts.Length,
          BufferUsage.WriteOnly
        )

      vb.SetData(verts)

      let ib =
        new IndexBuffer(
          gd,
          IndexElementSize.SixteenBits,
          indices.Length,
          BufferUsage.WriteOnly
        )

      ib.SetData(indices)

      let s = {
        Effect = effect
        SphereVerts = vb
        SphereIndices = ib
        SpherePrimCount = indices.Length / 3
        NoCullNoDepth = new RasterizerState(CullMode = CullMode.None)
        DepthOff = new DepthStencilState(DepthBufferEnable = false)
      }

      state <- ValueSome s
      s

  /// Draws the starry sky dome. MUST be called inside a camera scope so the
  /// SceneContext has valid view/projection matrices.
  let render
    (assets: IAssets)
    (totalTime: float32)
    (cameraPosNumerics: System.Numerics.Vector3)
    (buffer: RenderBuffer3D)
    =
    let camPos =
      Vector3(cameraPosNumerics.X, cameraPosNumerics.Y, cameraPosNumerics.Z)

    buffer
    |> Draw3D.drawImmediate(fun scene ->
      let s = ensureState assets scene.Device
      let effect = s.Effect
      let gd = scene.Device

      // Upload uniforms.
      let horizonLoc = effect.Parameters.["horizonColor"]

      if horizonLoc <> null then
        horizonLoc.SetValue(
          Vector3(
            float32 ViewMath.skyHorizonColor.R / 255.0f,
            float32 ViewMath.skyHorizonColor.G / 255.0f,
            float32 ViewMath.skyHorizonColor.B / 255.0f
          )
        )

      let zenithLoc = effect.Parameters.["zenithColor"]

      if zenithLoc <> null then
        zenithLoc.SetValue(
          Vector3(
            float32 ViewMath.skyZenithColor.R / 255.0f,
            float32 ViewMath.skyZenithColor.G / 255.0f,
            float32 ViewMath.skyZenithColor.B / 255.0f
          )
        )

      let timeLoc = effect.Parameters.["time"]

      if timeLoc <> null then
        timeLoc.SetValue(totalTime)

      let vpLoc = effect.Parameters.["viewProj"]

      if vpLoc <> null then
        vpLoc.SetValue(scene.View * scene.Projection)

      let modelLoc = effect.Parameters.["matModel"]

      if modelLoc <> null then
        modelLoc.SetValue(
          Matrix.CreateScale(500.0f) * Matrix.CreateTranslation(camPos)
        )

      // Save + set render states: no culling (inside sphere), no depth write.
      let prevRS = gd.RasterizerState
      let prevDS = gd.DepthStencilState
      gd.RasterizerState <- s.NoCullNoDepth
      gd.DepthStencilState <- s.DepthOff

      gd.SetVertexBuffer(s.SphereVerts)
      gd.Indices <- s.SphereIndices

      for pass in effect.CurrentTechnique.Passes do
        pass.Apply()

        gd.DrawIndexedPrimitives(
          PrimitiveType.TriangleList,
          0,
          0,
          s.SpherePrimCount
        )

      // Restore render states.
      gd.RasterizerState <- prevRS
      gd.DepthStencilState <- prevDS)
    |> Draw3D.drop
