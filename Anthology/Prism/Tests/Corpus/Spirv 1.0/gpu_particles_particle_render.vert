#version 460 core

// Vertex-pulled billboard renderer. No vertex buffer: gl_InstanceIndex
// selects the particle slot, gl_VertexIndex selects the quad corner
// (assumes a non-indexed 4-vertex triangle-strip draw per instance).

struct Particle
{
    vec3 position;
    float age;
    vec3 velocity;
    float lifetime;
    float size;
    float rotation;
    uint seed;
    float padding0;
};

layout(std430, set = 0, binding = 0) readonly buffer ParticleBuffer
{
    Particle particles[];
};

layout(set = 0, binding = 1) uniform CameraUBO
{
    mat4 view;
    mat4 proj;
    mat4 viewProj;
} camera;

layout(set = 0, binding = 2) uniform ColorGradientUBO
{
    vec4 spawnColor;
    vec4 midColor;
    vec4 deathColor;
} colorGradient;

layout(location = 0) out vec2 outUV;
layout(location = 1) out vec4 outColor;
layout(location = 2) out float outViewDepth;

const vec2 kCorners[4] = vec2[4](
    vec2(-0.5, -0.5),
    vec2(0.5, -0.5),
    vec2(-0.5, 0.5),
    vec2(0.5, 0.5)
);

const vec2 kUVs[4] = vec2[4](
    vec2(0.0, 0.0),
    vec2(1.0, 0.0),
    vec2(0.0, 1.0),
    vec2(1.0, 1.0)
);

void main()
{
    uint particleIndex = uint(gl_InstanceIndex);
    uint cornerIndex = uint(gl_VertexIndex) % 4u;

    Particle p = particles[particleIndex];

    float lifeT = clamp(p.age / max(p.lifetime, 0.0001), 0.0, 1.0);

    float growth = smoothstep(0.0, 0.15, lifeT);
    float shrink = 1.0 - smoothstep(0.7, 1.0, lifeT);
    float sizeCurve = growth * shrink;
    float currentSize = p.size * sizeCurve;

    if (p.lifetime <= 0.0)
    {
        currentSize = 0.0;
    }

    // view matrix rows hold the world-space camera basis vectors
    vec3 camRight = vec3(camera.view[0][0], camera.view[1][0], camera.view[2][0]);
    vec3 camUp = vec3(camera.view[0][1], camera.view[1][1], camera.view[2][1]);

    vec2 corner = kCorners[cornerIndex];

    float c = cos(p.rotation);
    float s = sin(p.rotation);
    vec2 rotatedCorner = vec2(
        corner.x * c - corner.y * s,
        corner.x * s + corner.y * c
    );

    vec3 worldPos = p.position
        + camRight * rotatedCorner.x * currentSize
        + camUp * rotatedCorner.y * currentSize;

    gl_Position = camera.viewProj * vec4(worldPos, 1.0);

    vec4 viewPos = camera.view * vec4(worldPos, 1.0);
    outViewDepth = -viewPos.z;

    vec4 color;
    if (lifeT < 0.5)
    {
        color = mix(colorGradient.spawnColor, colorGradient.midColor, lifeT * 2.0);
    }
    else
    {
        color = mix(colorGradient.midColor, colorGradient.deathColor, (lifeT - 0.5) * 2.0);
    }

    outUV = kUVs[cornerIndex];
    outColor = color;
}
