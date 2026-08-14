#version 330

// Defli aura — vertex shader (raylib).
// The raylib batch supplies vertexPosition + mvp (default locations);
// the camera transform rides inside mvp, so the fragment shader can
// work in WORLD space while rasterization happens in screen space.

in vec3 vertexPosition;

uniform mat4 mvp;

out vec2 fragWorldPos;

void main()
{
    fragWorldPos = vertexPosition.xy;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
