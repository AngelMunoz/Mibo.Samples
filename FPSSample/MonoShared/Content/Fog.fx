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
#elif defined(SM6)
  #define VS_SHADERMODEL vs_6_0
  #define PS_SHADERMODEL ps_6_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

// Cross-profile texture/sampler declarations (see ForwardPbr.fx for rationale).
#if OPENGL
  #define DECLARE_TEX(name, slot) sampler2D name : register(s##slot)
  #define SAMPLE_TEX(name, uv) tex2D(name, uv)
#else
  #define DECLARE_TEX(name, slot) Texture2D name : register(t##slot); SamplerState name##Sampler : register(s##slot)
  #define SAMPLE_TEX(name, uv) name.Sample(name##Sampler, uv)
#endif

DECLARE_TEX(SceneTexture, 0);
DECLARE_TEX(DepthTexture, 1);

float3 fogColor;
float fogNear;
float fogFar;
float cameraNear;
float cameraFar;
float fogStrength;
float3 camPos;
float3 camForward;
float3 camRight;
float3 camUp;
float fovY;
float aspect;
float fogCeiling;
float fogDensity;

struct VSInput {
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput {
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VSOutput FogVS(VSInput input) {
    VSOutput output;
    output.Position = float4(input.Position, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 FogPS(VSOutput input) : SV_TARGET {
    float4 scene = SAMPLE_TEX(SceneTexture, input.TexCoord);
    float ndcZ = SAMPLE_TEX(DepthTexture, input.TexCoord).r;

    // Skybox / uncovered pixels: depth ≈ 1.0 (far plane), skip fog so the sky stays visible.
    // On DesktopGL the depth pre-pass writes clip.z/clip.w in [-1,1]; far plane = 1.0.
    if (ndcZ >= 0.999) {
        return scene;
    }

    // NDC z in [0,1] → positive view-space distance. MonoGame's projection matrix
    // maps view z to [0,1] on both DX and OpenGL backends.
    float dist = (cameraFar * cameraNear) / (cameraFar - ndcZ * (cameraFar - cameraNear));

    // Reconstruct world position from depth + camera basis vectors.
    float tanHalfFov = tan(fovY * 0.5);
    float2 ndc = float2(input.TexCoord.x * 2.0 - 1.0, 1.0 - input.TexCoord.y * 2.0);
    float3 worldPos = camPos
        + ndc.x * aspect * tanHalfFov * dist * camRight
        + ndc.y * tanHalfFov * dist * camUp
        + dist * camForward;

    // Distance fog (ramps from fogNear to fogFar).
    float distFog = saturate((dist - fogNear) / (fogFar - fogNear));

    // Height fog: dense below fogCeiling, thin above it.
    float heightFog = saturate((fogCeiling - worldPos.y) / fogCeiling);
    heightFog = pow(heightFog, fogDensity);

    // Combined: height fog applies at any distance (ground-level haze),
    // distance fog adds on top for far geometry. Capped at 1.0.
    float fog = saturate(heightFog * 0.7 + distFog * 0.3) * fogStrength;

    return float4(lerp(scene.rgb, fogColor, fog), scene.a);
}

technique Fog {
    pass P0 {
        VertexShader = compile VS_SHADERMODEL FogVS();
        PixelShader = compile PS_SHADERMODEL FogPS();
    }
}
