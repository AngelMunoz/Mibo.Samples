// Grayscale post-process for the FPS sample (MonoGame / HLSL).
// Desaturates the rendered scene by `intensity` (0 = full color, 1 = full gray).
// Driven from the view by the remaining HitEffectTimer.
//
// The framework's FullScreenQuad feeds clip-space positions + TEXCOORD0, so the
// vertex shader is a passthrough; the pixel shader samples the scene texture and
// mixes toward luminance.
//
// Uniform (set by name from F#):
//   intensity — desaturation amount [0..1]

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

float intensity;

struct VSInput {
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput {
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VSOutput GrayscaleVS(VSInput input) {
    VSOutput output;
    output.Position = float4(input.Position, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 GrayscalePS(VSOutput input) : SV_TARGET {
    float4 c = SAMPLE_TEX(SceneTexture, input.TexCoord);
    float gray = dot(c.rgb, float3(0.299, 0.587, 0.114));
    return float4(lerp(c.rgb, float3(gray, gray, gray), intensity), c.a);
}

technique Grayscale {
    pass P0 {
        VertexShader = compile VS_SHADERMODEL GrayscaleVS();
        PixelShader = compile PS_SHADERMODEL GrayscalePS();
    }
}
