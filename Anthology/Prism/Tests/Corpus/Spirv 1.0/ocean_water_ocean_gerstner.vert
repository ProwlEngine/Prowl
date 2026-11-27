#version 460 core

// Gerstner-wave ocean vertex shader: displaces a flat base grid and derives
// an analytic surface normal from the summed wave partial derivatives.

#define MAX_WAVES 8
#define PI 3.14159265359
#define TWO_PI 6.28318530718

struct GerstnerWave
{
    vec2 direction;
    float steepness;
    float wavelength;
    float speed;
};

layout(set = 0, binding = 0) uniform WaveUBO
{
    GerstnerWave waves[MAX_WAVES];
    float time;
    int waveCount;
} uWave;

layout(set = 0, binding = 1) uniform CameraUBO
{
    mat4 view;
    mat4 proj;
    mat4 model;
} uCamera;

layout(location = 0) in vec3 inPosition;

layout(location = 0) out vec3 outWorldPos;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out float outWaveHeight;

void main()
{
    vec4 worldPos4 = uCamera.model * vec4(inPosition, 1.0);
    vec2 xz = worldPos4.xz;

    vec3 displacement = vec3(0.0);

    // Partial derivatives of the displaced position with respect to base x and z,
    // biased by the flat-plane identity contribution.
    vec3 dPdx = vec3(1.0, 0.0, 0.0);
    vec3 dPdz = vec3(0.0, 0.0, 1.0);

    int count = min(uWave.waveCount, MAX_WAVES);
    for (int i = 0; i < count; ++i)
    {
        GerstnerWave w = uWave.waves[i];
        vec2 dir = normalize(w.direction);
        float k = TWO_PI / w.wavelength;
        float f = k * (dot(dir, xz) - w.speed * uWave.time);
        float sinF = sin(f);
        float cosF = cos(f);
        float amplitude = w.steepness / k;

        displacement.x += amplitude * dir.x * cosF;
        displacement.z += amplitude * dir.y * cosF;
        displacement.y += amplitude * sinF;

        // Analytic derivatives (real Gerstner tangent/binormal terms, not finite-difference).
        float steepSin = w.steepness * sinF;
        float steepCos = w.steepness * cosF;

        dPdx.x += -dir.x * dir.x * steepSin;
        dPdx.y += dir.x * steepCos;
        dPdx.z += -dir.x * dir.y * steepSin;

        dPdz.x += -dir.x * dir.y * steepSin;
        dPdz.y += dir.y * steepCos;
        dPdz.z += -dir.y * dir.y * steepSin;
    }

    vec3 worldPos = worldPos4.xyz + displacement;
    vec3 normal = normalize(cross(dPdz, dPdx));

    outWorldPos = worldPos;
    outNormal = normal;
    outWaveHeight = displacement.y;

    gl_Position = uCamera.proj * uCamera.view * vec4(worldPos, 1.0);
}
