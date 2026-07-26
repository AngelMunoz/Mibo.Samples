// Toon (cel) shader — a minimal custom shading effect for the MonoThreeD sample,
// exercised via Draw3D.beginEffect / Draw3D.endEffect (use case 2 of the v2 pipeline
// staging). Proves a user effect can inherit scene DATA (camera, lights, material,
// bones, shadows) by declaring the matching uniforms — it does NOT inherit the PBR
// shader itself (v2 spec §3).
//
// The shading model is banded N·L + a rim term (cheap, reads clearly as "toon"). It
// declares the directional + ambient lights, the albedo map/colour, the bone palette,
// and the shadow-sampling uniforms — so a toon-scoped draw inherits lighting,
// animation, and shadows by name, with absent uniforms no-op'd by SceneUpload.
//
// §6 compliance (same as ForwardPbr.fx):
//  - §6.1: plain float4x4, mul(position, matrix) vector-LEFT.
//  - §6.3: OpenGL capped at SM3.0. Shadow sampling uses tex2Dlod (gradient-free) so it
//          composes with [loop]+break light loops; texel size comes in as a uniform.
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

#define MAX_BONES 128
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
// Lights (ambient + 1 directional — the toon model only uses these)
// ------------------------------------------------------------------

float3 ambientColor;
float ambientIntensity;

float3 dirLightDir;
float3 dirLightColor;
float dirLightIntensity;
int dirLightCastsShadows;

// ------------------------------------------------------------------
// Shadow atlas (opt-in by declaration). Manual 3x3 PCF, gradient-free
// (tex2Dlod), matching ForwardPbr.fx so a toon-scoped draw can sample shadows.
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

// Index for the directional caster (slot 0 by convention; SceneUpload leaves this
// uniform unset → defaults to 0). The arrays MUST be indexed dynamically here:
// with a constant [0] index the GL/MojoShader path trims the uniform arrays to a
// single element, but the effect parameter table still reports MAX_SHADOW_CASTERS
// elements — MonoGame's GL ConstantBuffer.Update then writes past the end of the
// shader's constant buffer and crashes in EffectPass.Apply (BlockCopy overflow).
// DX11/DX12/Vulkan reflection keeps the declared size, which is why only OpenGL
// crashed. ForwardPbr.fx avoids this by indexing with pointLightShadowIdx[i] /
// spotLightShadowIdx[j] (dynamic).
int dirLightShadowIdx;

// Mirrors ForwardPbr.fx's computeShadowAt/computeDirShadow: receiver-side bias
// (without it a flat caster that is also a receiver — snow ground —
// self-shadows across the whole frustum), clip-space frustum cull before the
// UV remap, and a Y-flip so the atlas lookup matches the DirectX viewport.
float computeDirShadow(float3 worldPos) {
  if (dirLightCastsShadows == 0)
    return 1.0;

  // Directional caster is registered first (slot 0 by convention).
  float4 sc = mul(float4(worldPos, 1.0), shadowViewProjs[dirLightShadowIdx]);
  float3 ndc = sc.xyz / sc.w;

  // Outside the shadow frustum → fully lit (no shadow).
  if (ndc.z > 1.0)
    return 1.0;

  // ndc.z stays in clip space [-1,1] (raw clip.z/clip.w in the atlas color
  // target). Only xy is remapped to [0,1] for atlas UV lookup — cull first.
  if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0)
    return 1.0;

  float4 uvOff = shadowUVOffsets[dirLightShadowIdx];
  // Flip y: DirectX viewports map clip.y=1 to the top of the render target,
  // while texture v increases downward.
  float2 atlasUV = float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5) * uvOff.zw + uvOff.xy;

  // Receiver-side bias: shrink the receiver depth so a surface doesn't shadow
  // itself. This is what stops flat snow/ground from self-shadowing.
  float recvZ = ndc.z - shadowBiases[dirLightShadowIdx];

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
// Matrices + camera
// ------------------------------------------------------------------

float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;
float3 cameraPos;

struct VS_INPUT {
  float3 Position : POSITION0;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : NORMAL0;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : TEXCOORD1;
  float3 WorldPos : TEXCOORD2;
};

VS_OUTPUT VS_Standard(VS_INPUT input) {
  VS_OUTPUT output;
  float4 world = mul(float4(input.Position, 1.0), matModel);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(input.Normal, (float3x3) normalMatrix);
  output.WorldPos = world.xyz;
  return output;
}

// ------------------------------------------------------------------
// Skinned vertex shader — 4-bone linear blend skinning, mirrors ForwardPbr.fx
// VS_Skinned so a toon-scoped AnimatedModel inherits its bone palette.
// ------------------------------------------------------------------

float4x4 boneMatrices[MAX_BONES];

struct VS_INPUT_SKINNED {
  float3 Position   : POSITION0;
  float2 TexCoord   : TEXCOORD0;
  float3 Normal     : NORMAL0;
  float4 BoneWeights: BLENDWEIGHT0;
  int4   BoneIndices: BLENDINDICES0;
};

VS_OUTPUT VS_Skinned(VS_INPUT_SKINNED input) {
  VS_OUTPUT output;

  float4x4 skin =
    input.BoneWeights.x * boneMatrices[input.BoneIndices.x] +
    input.BoneWeights.y * boneMatrices[input.BoneIndices.y] +
    input.BoneWeights.z * boneMatrices[input.BoneIndices.z] +
    input.BoneWeights.w * boneMatrices[input.BoneIndices.w];

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  float4 world = mul(skinnedPos, matModel);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(skinnedN, (float3x3) normalMatrix);
  output.WorldPos = world.xyz;
  return output;
}

// ------------------------------------------------------------------
// Instanced vertex shader — per-sub-mesh instancing opt-in.
//
// The pipeline selects `technique Instanced` for instanced draws (see
// docs/graphics3d/instancing.md and docs/shader-uniforms.md § Instancing).
// The per-instance 4×4 world matrix arrives as four float4 rows on
// TEXCOORD1..4 (stream 1, usage indices 1-4 so they don't collide with the
// mesh's TEXCOORD0 on stream 0). `matModel`/`normalMatrix` are uploaded as
// identity for instanced draws, so the world + normal transforms are derived
// in-shader from the instance matrix — matching Instanced.fx and
// forwardVertexInstanced. PS_Main is reused unchanged.
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
// Fragment: banded N·L toon shading + rim term.
// ------------------------------------------------------------------

// Quantise the diffuse term into discrete bands → the cel-shaded look.
float toonBand(float NdotL) {
  // 3 bands: shadow / mid / lit. Smoothstep softens the step edges.
  float b = smoothstep(0.0, 0.05, NdotL) * 0.4;       // mid band
  b += smoothstep(0.5, 0.55, NdotL) * 0.6;            // lit band
  return b;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float2 uv = input.TexCoord * tiling;
  float4 texColor = SAMPLE_TEX(texture0, uv) * albedoColor;
  float3 albedo = texColor.rgb;

  float3 N = normalize(input.Normal);
  float3 V = normalize(cameraPos - input.WorldPos);

  // Ambient base.
  float3 ambient = ambientColor * albedo * ambientIntensity;

  // Directional (L points toward the light; dirLightDir points along travel).
  float3 L = normalize(-dirLightDir);
  float NdotL = dot(N, L);
  float band = toonBand(max(NdotL, 0.0));
  float shadow = computeDirShadow(input.WorldPos);
  float3 dir = dirLightColor * dirLightIntensity * albedo * band * shadow;

  // Rim: brighten edges facing away from the camera for a toon outline feel.
  float rim = 1.0 - max(dot(N, V), 0.0);
  rim = smoothstep(0.6, 1.0, rim);
  float3 rimColor = dirLightColor * rim * 0.4;

  float3 result = ambient + dir + rimColor;
  return float4(result, texColor.a * opacity);
}

// ------------------------------------------------------------------
// Techniques
// ------------------------------------------------------------------

technique Standard {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Standard();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique Skinned {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Skinned();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique Instanced {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Instanced();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
