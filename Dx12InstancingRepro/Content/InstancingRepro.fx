// Minimal repro: two techniques that expose DX12 mgfx shader-profile gaps.
//
// 1. VS_TextureFetch — samples a texture in the VERTEX stage (VTF).
//    The MonoGame DX12 backend never wires vertex-stage SRVs to the VS,
//    so paletteTex reads as zeros and the vertex collapses to the origin.
//
// 2. VS_GroupedUniform — reads a float4x4[] array out of $Globals.
//    The mgfx DX12 reflection parser (ShaderProfile.DirectX12.cs CBufferParam
//    regex) fails to register float4x4[N] array parameters into the effect's
//    parameter table, so effect.Parameters["bonePaletteGroup"] returns null at
//    runtime — the upload silently no-ops and the VS reads a zeroed cbuffer.
//
// Both techniques render nothing on DX12. On DX11 and Vulkan both work.

float4x4 viewProj : VIEWPROJ;

// --- VTF path (technique 1) ---------------------------------------------
// A texture sampled from the vertex shader. On DX12 the SRV is bound but the
// VS stage never sees it — SampleLevel returns 0 — regardless of slot/content.
Texture2D paletteTex : register(t6);
SamplerState paletteTexSampler : register(s6);
float2 paletteTexSize;

float4 paletteBoneRow(int row, float instance) {
    float2 uv = float2(
        (float(row) + 0.5) / paletteTexSize.x,
        (instance + 0.5) / paletteTexSize.y);
    return paletteTex.SampleLevel(paletteTexSampler, uv, 0);
}

struct VS_IN_VTF {
    float3 Position : POSITION0;
    float  PaletteOffset : TEXCOORD0;  // which row in the palette texture
};

struct VS_OUT {
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
};

VS_OUT VS_TextureFetch(VS_IN_VTF input) {
    VS_OUT output;
    // Fetch row 0 of instance's bone matrix from the palette texture.
    float4 boneRow = paletteBoneRow(0, input.PaletteOffset);
    float4 world = float4(input.Position, 1.0) + boneRow;
    output.Position = mul(world, viewProj);
    // Color the vertex with the fetched value so we can SEE whether the VS
    // read the texture: if VTF works the quad is tinted; if VTF is broken
    // (DX12) boneRow is 0 and the quad is black / collapses to the origin.
    output.Color = boneRow;
    return output;
}

// --- Grouped-uniform path (technique 2) ----------------------------------
// A float4x4[] array in $Globals. The mgfx DX12 reflection regex for
// ShaderProfile.DirectX12.cs does not register float4x4[N] array params,
// so effect.Parameters["bonePaletteGroup"] is null at runtime on DX12.
#define MAX_BONES 128
float4x4 bonePaletteGroup[MAX_BONES];
int groupBoneCount;

struct VS_IN_GROUP {
    float3 Position : POSITION0;
    int   BoneIndex : BLENDINDICES0;  // which matrix in bonePaletteGroup
};

VS_OUT VS_GroupedUniform(VS_IN_GROUP input) {
    VS_OUT output;
    float4x4 skin = bonePaletteGroup[input.BoneIndex];
    float4 world = mul(float4(input.Position, 1.0), skin);
    output.Position = mul(world, viewProj);
    // If the array upload silently no-ops (DX12), skin is all zeros and the
    // vertex collapses to the origin → nothing rasterizes.
    output.Color = float4(1, 1, 0, 1);
    return output;
}

float4 PS_Main(VS_OUT input) : SV_TARGET {
    return input.Color;
}

technique TextureFetch {
    pass P0 {
        VertexShader = compile vs_6_0 VS_TextureFetch();
        PixelShader  = compile ps_6_0 PS_Main();
    }
}

technique GroupedUniform {
    pass P0 {
        VertexShader = compile vs_6_0 VS_GroupedUniform();
        PixelShader  = compile ps_6_0 PS_Main();
    }
}
