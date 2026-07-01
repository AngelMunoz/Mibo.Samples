// Starry procedural skybox for the FPS sample (MonoGame / HLSL).
// Ported from the raylib GLSL shader. Generates multi-layer twinkling
// stars + soft nebula clouds on the inside of a large sphere.
//
// Uniforms (set by name from F#):
//   viewProj       — camera view * projection matrix
//   matModel       — sphere world matrix (scale + translate to camera)
//   time           — elapsed seconds (twinkle animation)
//   horizonColor   — sky gradient bottom color (float3)
//   zenithColor    — sky gradient top color (float3)

#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

float4x4 viewProj : WORLDVIEWPROJECTION;
float4x4 matModel : WORLD;
float time : TIME;
float3 horizonColor;
float3 zenithColor;

struct VSInput {
    float3 Position : POSITION0;
};

struct VSOutput {
    float4 Position : POSITION0;
    float3 Dir      : TEXCOORD0;
};

VSOutput SkyboxVS(VSInput input) {
    VSOutput output;
    output.Dir = normalize(input.Position);
    float4 worldPos = mul(float4(input.Position, 1.0), matModel);
    output.Position = mul(worldPos, viewProj);
    return output;
}

// 3D hash — deterministic pseudo-random per grid cell.
float hash13(float3 p) {
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

// Value noise for soft nebula clouds.
float vnoise(float3 x) {
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(hash13(i + float3(0,0,0)), hash13(i + float3(1,0,0)), f.x),
             lerp(hash13(i + float3(0,1,0)), hash13(i + float3(1,1,0)), f.x), f.y),
        lerp(lerp(hash13(i + float3(0,0,1)), hash13(i + float3(1,0,1)), f.x),
             lerp(hash13(i + float3(0,1,1)), hash13(i + float3(1,1,1)), f.x), f.y),
        f.z);
}

float fbm(float3 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++) {
        v += a * vnoise(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

// Grid-based star field — each cell may contain one star with a random
// sub-cell position, brightness, and twinkle phase.
float starField(float3 dir, float density, float brightness) {
    float3 p = dir * density;
    float3 cell = floor(p);
    float3 f = frac(p);

    float h = hash13(cell);
    // Sparse stars: only ~5% of cells light up.
    if (h < 0.95) return 0.0;

    // Random sub-cell offset so stars aren't on a perfect grid.
    float3 off = float3(hash13(cell + 1.7), hash13(cell + 3.3), hash13(cell + 7.1)) - 0.5;
    float d = length(f - 0.5 - off * 0.6);

    // Sharp circular star with soft glow.
    float star = smoothstep(0.12, 0.0, d);
    float glow = smoothstep(0.35, 0.0, d) * 0.25;

    // Twinkle.
    float twinkle = 0.6 + 0.4 * sin(time * 2.0 + h * 100.0);

    return (star + glow) * brightness * twinkle;
}

float4 SkyboxPS(VSOutput input) : COLOR0 {
    float3 dir = normalize(input.Dir);

    // Gradient sky.
    float t = clamp(dir.y * 0.5 + 0.5, 0.0, 1.0);
    float3 sky = lerp(horizonColor, zenithColor, pow(t, 0.8));

    // Nebula bands.
    float nebula = fbm(dir * 3.0 + float3(time * 0.02, 0.0, 0.0));
    float3 nebCol1 = float3(0.15, 0.02, 0.20);
    float3 nebCol2 = float3(0.02, 0.08, 0.15);
    float nebMask = smoothstep(0.45, 0.75, nebula);
    sky += lerp(nebCol1, nebCol2, nebula) * nebMask * 0.5;

    // Star layers (three densities for depth).
    float stars = 0.0;
    stars += starField(dir, 40.0,  1.0);  // bright nearby
    stars += starField(dir, 80.0,  0.7);  // medium
    stars += starField(dir, 150.0, 0.4);  // faint distant

    // Star color: warm white with occasional blue tint.
    float starColor = hash13(floor(dir * 80.0) + 11.0);
    float3 starCol = lerp(float3(1.0, 0.95, 0.8), float3(0.7, 0.85, 1.0), starColor);
    sky += stars * starCol;

    return float4(sky, 1.0);
}

technique Skybox {
    pass P0 {
        VertexShader = compile VS_SHADERMODEL SkyboxVS();
        PixelShader = compile PS_SHADERMODEL SkyboxPS();
    }
}
