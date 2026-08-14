#version 330

// Defli aura — fragment shader (raylib).
// A soft radial glow with a bright band at the rim, a breathing pulse
// and an angular shimmer. Drawn additively over a filled circle; every
// pixel of the disc is owned by this shader.

in vec2 fragWorldPos;

out vec4 finalColor;

uniform vec2 auraCenter;  // world-space disc center
uniform float auraRadius; // world-space radius
uniform float auraTime;   // seconds, from the frame's GameTime
uniform vec4 auraColor;   // rgb + intensity (0..1)
uniform float auraRing;   // 0..1 — where the bright band sits

void main()
{
    vec2 delta = fragWorldPos - auraCenter;
    float d = length(delta);
    float t = d / max(auraRadius, 0.0001);

    // Soft wash: full at the center, transparent exactly at the rim.
    float wash = 1.0 - smoothstep(0.0, 1.0, t);

    // Bright band at auraRing, gaussian-shaped.
    float band = exp(-pow((t - auraRing) * 6.0, 2.0));

    // Breathing pulse + angular shimmer, driven by the frame clock.
    float angle = atan(delta.y, delta.x);
    float shimmer = 0.85 + 0.15 * sin(angle * 3.0 + auraTime * 2.5);
    float pulse = 0.9 + 0.1 * sin(auraTime * 2.0);

    float a = clamp(wash * 0.5 + band * 0.85, 0.0, 1.0) * shimmer * pulse;

    finalColor = vec4(auraColor.rgb, a * auraColor.a);
}
