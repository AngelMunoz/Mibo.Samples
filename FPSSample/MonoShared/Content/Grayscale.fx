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
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

Texture2D SceneTexture;
sampler2D SceneSampler = sampler_state { Texture = <SceneTexture>; };

float intensity;

struct VSInput {
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput {
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

VSOutput GrayscaleVS(VSInput input) {
    VSOutput output;
    output.Position = float4(input.Position, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 GrayscalePS(VSOutput input) : COLOR0 {
    float4 c = tex2D(SceneSampler, input.TexCoord);
    float gray = dot(c.rgb, float3(0.299, 0.587, 0.114));
    return float4(lerp(c.rgb, float3(gray, gray, gray), intensity), c.a);
}

technique Grayscale {
    pass P0 {
        VertexShader = compile VS_SHADERMODEL GrayscaleVS();
        PixelShader = compile PS_SHADERMODEL GrayscalePS();
    }
}
