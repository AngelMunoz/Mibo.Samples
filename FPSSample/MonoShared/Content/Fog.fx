// Distance fog post-process for the FPS sample (MonoGame / HLSL).
// Fades the lit scene toward a fog color with distance, using the camera-POV depth
// produced by the pipeline's depth pre-pass (Command3D.EnableDepthPrePass). The scene
// is already lit when this runs, so torch light near the viewer survives while distant
// geometry fades into the fog — what a foggy night reads as.
//
// The depth pre-pass writes NDC z in [0,1] (0 = near, 1 = far); we linearize it back to
// view-space distance with the camera's near/far planes, then apply a smooth near/far
// fog window.
//
// Robustness: when the pipeline can't produce a depth texture this frame, the caller sets
// fogStrength = 0 (and binds the scene texture into the depth slot to keep the sampler
// valid), so the pixel shader passes the scene through unchanged — never a blank screen.
//
// The framework's FullScreenQuad feeds clip-space positions + TEXCOORD0, so the vertex
// shader is a passthrough.
//
// Uniforms (set by name from F#):
//   fogColor    — RGB the scene fades toward
//   fogNear     — view-space distance where fog begins (world units)
//   fogFar      — view-space distance where fog is fully opaque (world units)
//   cameraNear  — camera near plane
//   cameraFar   — camera far plane
//   fogStrength — 1.0 to apply fog from depth, 0.0 to pass the scene through unchanged

#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

Texture2D SceneTexture;
sampler2D SceneSampler = sampler_state { Texture = <SceneTexture>; };

Texture2D DepthTexture;
sampler2D DepthSampler = sampler_state { Texture = <DepthTexture>; };

float3 fogColor;
float fogNear;
float fogFar;
float cameraNear;
float cameraFar;
float fogStrength;

struct VSInput {
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput {
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

VSOutput FogVS(VSInput input) {
    VSOutput output;
    output.Position = float4(input.Position, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 FogPS(VSOutput input) : COLOR0 {
    float4 scene = tex2D(SceneSampler, input.TexCoord);
    float ndcZ = tex2D(DepthSampler, input.TexCoord).r;

    // NDC z in [0,1] → positive view-space distance. The projection maps view-space
    // distance (between near and far) linearly-in-1/z to [0,1]; the inverse is:
    //   d = (far * near) / (far - ndcZ * (far - near))
    float dist = (cameraFar * cameraNear) / (cameraFar - ndcZ * (cameraFar - cameraNear));

    // Smooth fog window: 0 before fogNear, 1 after fogFar.
    float fog = saturate((dist - fogNear) / (fogFar - fogNear)) * fogStrength;

    return float4(lerp(scene.rgb, fogColor, fog), scene.a);
}

technique Fog {
    pass P0 {
        VertexShader = compile VS_SHADERMODEL FogVS();
        PixelShader = compile PS_SHADERMODEL FogPS();
    }
}
