// Aura.fx — a fresnel rim shader for the boss body aura.
//
// Drawn through Command3D.DrawImmediate (NOT beginEffect): a translucent
// custom shader needs an alpha blend + depth-read, and the beginEffect scope
// runs its draws inline with the pass's opaque blend/depth. drawImmediate
// gives full device control, so the caller sets BlendState.AlphaBlend +
// DepthStencilState.DepthRead, uploads the matrices/scene data this effect
// declares, and draws the unit sphere (Primitive3D.Sphere) scaled around the
// boss.
//
// The fragment brightens toward the silhouette (fresnel) so the sphere reads
// as a soft shell/halo around the boss rather than a flat bubble: the rim is
// opaque-tinted, the facing center is near-transparent.
#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#elif defined(SM6)
  // Vulkan requires Shader Model 6.0 (DXIL). MGCB defines SM6 for DesktopVK.
  #define VS_SHADERMODEL vs_6_0
  #define PS_SHADERMODEL ps_6_0
#else
  // DirectX 11/12 use Shader Model 5.0.
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

// Matrices + camera (uploaded by the caller from the SceneContext — drawImmediate
// does not inherit the beginEffect scene upload).
float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;
float3 cameraPos;

// Aura tuning (uploaded by the caller).
float3 auraColor;     // rgb tint
float auraPower;      // fresnel exponent (higher = tighter rim)
float auraIntensity;  // overall strength (0..1ish)

struct VS_INPUT {
  float3 Position : POSITION0;
  float3 Normal   : NORMAL0;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float3 WorldPos : TEXCOORD0;
  float3 WorldNormal : TEXCOORD1;
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT o;
  float4 world = mul(float4(input.Position, 1.0), matModel);
  o.Position = mul(world, viewProj);
  o.WorldPos = world.xyz;
  o.WorldNormal = mul(input.Normal, (float3x3)normalMatrix);
  return o;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float3 N = normalize(input.WorldNormal);
  float3 V = normalize(cameraPos - input.WorldPos);
  // Fresnel: ~0 facing the camera, ~1 at the silhouette. saturate keeps the
  // pow base non-negative (silences X3571; max() already bounds it to [0,1]).
  float fresnel = pow(saturate(1.0 - max(dot(N, V), 0.0)), auraPower);
  // A small floor tints the whole volume faintly; the rim is brightest.
  float a = clamp(auraIntensity * (0.20 + 0.80 * fresnel), 0.0, 1.0);
  return float4(auraColor, a);
}

technique Standard {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Main();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
