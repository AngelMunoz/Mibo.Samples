// mgfxc platform defines (see Mibo/ForwardPbr.fx): OPENGL (DesktopGL),
// HLSL (DX11), HLSL+SM6 (DX12), VULKAN+SM6 (Vulkan).
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

// Defli aura — HLSL effect (MonoGame, built through Content.mgcb).
//
// Vertex contract: the main Mibo shader shape (ForwardPbr.fx) — no
// vertex colors; the color rides the AuraColor uniform. The circle's
// bbox-mapped UVs carry the radial coordinate, so the pixel shader
// needs no center/radius uniforms: t = length(uv - 0.5) * 2 is 0 at
// the center and 1 at the rim.
//
// MatrixTransform follows the documented 2D view-projection contract
// (docs/shader-uniforms.md): world × screen orthographic projection.
//
// AuraMask is a radial falloff baked once at boot (white center →
// transparent rim) — it is the soft wash; the shader adds the bright
// rim band, the breathing pulse and the angular shimmer on top.

// Cross-profile texture/sampler declarations (see ForwardPbr.fx for rationale).
#if OPENGL
  #define DECLARE_TEX(name, slot) sampler2D name : register(s##slot)
  #define SAMPLE_TEX(name, uv) tex2D(name, uv)
#else
  #define DECLARE_TEX(name, slot) Texture2D name : register(t##slot); SamplerState name##Sampler : register(s##slot)
  #define SAMPLE_TEX(name, uv) name.Sample(name##Sampler, uv)
#endif

float4x4 MatrixTransform;

float AuraTime;
float4 AuraColor;
float AuraRing;

DECLARE_TEX(AuraMask, 0);

struct VS_INPUT {
  float3 Position : POSITION0;
  float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT output;
  output.Position = mul(float4(input.Position.xyz, 1.0f), MatrixTransform);
  output.TexCoord = input.TexCoord;
  return output;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float2 delta = input.TexCoord - float2(0.5f, 0.5f);
  float t = length(delta) * 2.0f;

  // Soft wash (the baked mask) + bright band at AuraRing.
  float mask = SAMPLE_TEX(AuraMask, input.TexCoord).a;
  float band = exp(-pow((t - AuraRing) * 6.0f, 2.0f));

  // Breathing pulse + angular shimmer, driven by the frame clock.
  float angle = atan2(delta.y, delta.x);
  float shimmer = 0.85f + 0.15f * sin(angle * 3.0f + AuraTime * 2.5f);
  float pulse = 0.9f + 0.1f * sin(AuraTime * 2.0f);

  float a = clamp(mask * 0.6f + band * 0.9f, 0.0f, 1.0f) * shimmer * pulse;

  return float4(AuraColor.rgb, a * AuraColor.a);
}

technique Aura {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Main();
    PixelShader  = compile PS_SHADERMODEL PS_Main();
  }
}
