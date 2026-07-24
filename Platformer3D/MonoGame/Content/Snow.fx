// Snow shader — frosty/crystallized instanced look for snow biome blocks.
//
// Validation shader for the grid-instanced-shaders feature: applies a custom
// effect to a whole biome (snow) via the per-key resolver on instanced grid
// draws, distinct from the banded toon shader used on grass LargeBlocks. Like
// Toon.fx it inherits scene DATA (camera, lights, material, shadows) by name
// via SceneUpload and writes only its shading term — it does NOT inherit the
// PBR shader itself.
//
// The shading model is a cool frosty base + a fresnel crystalline rim + a
// time-driven sparkle (specular glints that drift across the surface), so snow
// reads as icy/crystalline rather than flat matte. Declares `time` + camera
// (fresnel/sparkle) in addition to the light/material uniforms.
//
// §6 compliance (same as ForwardPbr.fx / Toon.fx):
//  - §6.1: plain float4x4, mul(position, matrix) vector-LEFT.
//  - §6.3: OpenGL capped at SM3.0. Shadow sampling uses tex2Dlod (gradient-free).
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

#define MAX_SHADOW_CASTERS 16

// Cross-profile texture/sampler declarations (see ForwardPbr.fx for rationale).
#if OPENGL
  #define DECLARE_TEX(name, slot) sampler2D name : register(s##slot)
  #define SAMPLE_TEX(name, uv) tex2D(name, uv)
  #define SAMPLE_TEX_LOD(name, uv, lod) tex2Dlod(name, float4(uv, 0.0, lod))
#else
  #define DECLARE_TEX(name, slot) Texture2D name : register(t##slot); SamplerState name##Sampler : register(s##slot)
  #define SAMPLE_TEX(name, uv) name.Sample(name##Sampler, uv)
  #define SAMPLE_TEX_LOD(name, uv, lod) name.SampleLevel(name##Sampler, uv, lod)
#endif

// ------------------------------------------------------------------
// Samplers + material (SceneUpload uploads these by name when declared)
// ------------------------------------------------------------------

DECLARE_TEX(texture0, 0); // albedo

float4 albedoColor;
float opacity;
float2 tiling;

// ------------------------------------------------------------------
// Lights (ambient + 1 directional — the snow model only uses these)
// ------------------------------------------------------------------

float3 ambientColor;
float ambientIntensity;

float3 dirLightDir;
float3 dirLightColor;
float dirLightIntensity;
int dirLightCastsShadows;

// ------------------------------------------------------------------
// Shadow atlas (opt-in by declaration). Manual 3x3 PCF, gradient-free
// (tex2Dlod), matching ForwardPbr.fx / Toon.fx so a snow-scoped draw samples
// shadows.
// ------------------------------------------------------------------

#if OPENGL
sampler2D shadowAtlas : register(s5);
#else
DECLARE_TEX(shadowAtlas, 5);
#endif
float4x4 shadowViewProjs[MAX_SHADOW_CASTERS];
float4 shadowUVOffsets[MAX_SHADOW_CASTERS];
float2 shadowTexelSize;
float shadowBiases[MAX_SHADOW_CASTERS];

// Mirrors ForwardPbr.fx's computeShadowAt/computeDirShadow: receiver-side bias
// (without it a flat caster that is also a receiver — snow ground —
// self-shadows across the whole frustum), clip-space frustum cull before the
// UV remap, and a Y-flip so the atlas lookup matches the DirectX viewport.
float computeDirShadow(float3 worldPos) {
  if (dirLightCastsShadows == 0)
    return 1.0;

  // Directional caster is registered first (slot 0 by convention).
  float4 sc = mul(float4(worldPos, 1.0), shadowViewProjs[0]);
  float3 ndc = sc.xyz / sc.w;

  // Outside the shadow frustum → fully lit (no shadow).
  if (ndc.z > 1.0)
    return 1.0;

  // ndc.z stays in clip space [-1,1] (raw clip.z/clip.w in the atlas color
  // target). Only xy is remapped to [0,1] for atlas UV lookup — cull first.
  if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0)
    return 1.0;

  float4 uvOff = shadowUVOffsets[0];
  // Flip y: DirectX viewports map clip.y=1 to the top of the render target,
  // while texture v increases downward.
  float2 atlasUV = float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5) * uvOff.zw + uvOff.xy;

  // Receiver-side bias: shrink the receiver depth so a surface doesn't shadow
  // itself. This is what stops flat snow/ground from self-shadowing.
  float recvZ = ndc.z - shadowBiases[0];

  float shadow = 0.0;
  [unroll]
  for (int x = -1; x <= 1; x++) {
    [unroll]
    for (int y = -1; y <= 1; y++) {
      float2 sampleUV = atlasUV + float2(float(x), float(y)) * shadowTexelSize;
      float d = SAMPLE_TEX_LOD(shadowAtlas, sampleUV, 0.0).r;
      shadow += (recvZ > d) ? 0.0 : 1.0;
    }
  }
  return shadow / 9.0;
}

// ------------------------------------------------------------------
// Matrices + camera + clock
// ------------------------------------------------------------------

float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;
float3 cameraPos;
float time;

// ------------------------------------------------------------------
// Instanced vertex shader — per-sub-mesh instancing opt-in.
//
// `technique Instanced` is selected for instanced draws. The per-instance 4×4
// world arrives as four float4 rows on TEXCOORD1..4 (stream 1). matModel /
// normalMatrix are identity for instanced draws, so the world + normal are
// derived in-shader from the instance matrix.
// ------------------------------------------------------------------

struct VS_INPUT_INSTANCED {
  float3 Position : POSITION0;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : NORMAL0;
  // Per-instance (stream 1) — 4 rows composing a 4x4 world matrix.
  float4 Row0     : TEXCOORD1;
  float4 Row1     : TEXCOORD2;
  float4 Row2     : TEXCOORD3;
  float4 Row3     : TEXCOORD4;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : TEXCOORD1;
  float3 WorldPos : TEXCOORD2;
};

VS_OUTPUT VS_Instanced(VS_INPUT_INSTANCED input) {
  VS_OUTPUT output;
  float4x4 world = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 worldPos = mul(float4(input.Position, 1.0), world);
  output.Position = mul(worldPos, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(input.Normal, (float3x3)world);
  output.WorldPos = worldPos.xyz;
  return output;
}

// ------------------------------------------------------------------
// Fragment: frosty/crystallized snow shading.
// ------------------------------------------------------------------

// Pseudo-random hash for per-surface sparkle variation (cheap, no texture).
float hash23(float3 p) {
  float3 q = frac(p * 0.1031);
  q = q + dot(q, q.yzx + 33.33);
  return frac((q.x + q.y) * q.z);
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float2 uv = input.TexCoord * tiling;
  float4 texColor = SAMPLE_TEX(texture0, uv) * albedoColor;
  float3 albedo = texColor.rgb;

  float3 N = normalize(input.Normal);
  float3 V = normalize(cameraPos - input.WorldPos);
  float3 L = normalize(-dirLightDir);

  // Frosty cool tint — bias the albedo toward an icy blue-white.
  float3 frostTint = float3(0.80, 0.88, 1.0);
  float3 frosty = lerp(albedo, albedo * frostTint + float3(0.05, 0.07, 0.10), 0.5);

  // Ambient base.
  float3 ambient = ambientColor * frosty * ambientIntensity;

  // Soft diffuse (not banded — snow is smooth, unlike the toon look).
  float NdotL = max(dot(N, L), 0.0);
  float shadow = computeDirShadow(input.WorldPos);
  float3 diffuse = dirLightColor * dirLightIntensity * frosty * NdotL * shadow;

  // Crystalline fresnel rim — brightens glancing angles for an icy sheen.
  float fresnel = pow(1.0 - max(dot(N, V), 0.0), 3.0);
  float3 rim = dirLightColor * fresnel * 0.6;

  // Time-driven sparkle: drifting specular glints keyed off world position so
  // they sit on the surface rather than sliding with the camera. Quantise the
  // position to discrete crystal cells; a glint fires when its phase aligns.
  float3 cell = floor(input.WorldPos * 8.0);
  float h = hash23(cell);
  float phase = h * 6.2831853 + time * 2.0;
  float glint = pow(max(0.0, sin(phase)), 32.0);
  // Only glint where the surface faces the view-ish direction.
  glint *= smoothstep(0.2, 0.8, dot(N, V));
  float3 sparkle = dirLightColor * glint * 0.8;

  float3 result = ambient + diffuse + rim + sparkle;
  return float4(result, texColor.a * opacity);
}

// ------------------------------------------------------------------
// Technique
// ------------------------------------------------------------------

technique Instanced {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Instanced();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
