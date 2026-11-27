#version 460 core

layout(location = 0) out vec4 OutputR;
layout(location = 1) out vec4 OutputG;
layout(location = 2) out vec4 OutputB;

layout(location = 3) in vec2 TexCoords;

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 _Resolution;
    float _FocusStrength;
    float _ManualFocusPoint;
    float _MaxBlurRadius;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _CameraDepthTexture;

// Kernel constants
#define KERNEL_RADIUS 8
#define KERNEL_COUNT 17

// Final composition weights for both kernels
const vec2 FinalWeights_Kernel0 = vec2(0.411259, -0.548794);
const vec2 FinalWeights_Kernel1 = vec2(0.513282, 4.561110);

// Combined kernel coefficients (xy: Kernel0, zw: Kernel1)
const vec4 CombinedKernels[KERNEL_COUNT] = vec4[](
    vec4( 0.014096, -0.022658, 0.000115, 0.009116),
    vec4(-0.020612, -0.025574, 0.005324, 0.013416),
    vec4(-0.038708,  0.006957, 0.013753, 0.016519),
    vec4(-0.021449,  0.040468, 0.024700, 0.017215),
    vec4( 0.013015,  0.050223, 0.036693, 0.015064),
    vec4( 0.042178,  0.038585, 0.047976, 0.010684),
    vec4( 0.057972,  0.019812, 0.057015, 0.005570),
    vec4( 0.063647,  0.005252, 0.062782, 0.001529),
    vec4( 0.064754,  0.000000, 0.064754, 0.000000),
    vec4( 0.063647,  0.005252, 0.062782, 0.001529),
    vec4( 0.057972,  0.019812, 0.057015, 0.005570),
    vec4( 0.042178,  0.038585, 0.047976, 0.010684),
    vec4( 0.013015,  0.050223, 0.036693, 0.015064),
    vec4(-0.021449,  0.040468, 0.024700, 0.017215),
    vec4(-0.038708,  0.006957, 0.013753, 0.016519),
    vec4(-0.020612, -0.025574, 0.005324, 0.013416),
    vec4( 0.014096, -0.022658, 0.000115, 0.009116)
);

// Calculate Circle of Confusion
float calculateCoC(float depth, float focusPoint)
{
    float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
    float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
    float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
    return min(cocPixels, maxBlurPixels);
}

void main()
{
    float focusPoint = _ManualFocusPoint;

    float depth = texture(_CameraDepthTexture, TexCoords).x;
    float coc = calculateCoC(depth, focusPoint);
    float radius = coc / _Resolution.x / float(KERNEL_RADIUS);

    vec4 rVal = vec4(0.0);
    vec4 gVal = vec4(0.0);
    vec4 bVal = vec4(0.0);

    for (int i = 0; i < KERNEL_COUNT; i++)
    {
        int offset = i - KERNEL_RADIUS;
        vec2 coords = TexCoords + vec2(offset * radius, 0.0);
        coords = clamp(coords, vec2(0.0), vec2(1.0));

        vec3 image = texture(_MainTex, coords).rgb;
        vec4 kernels = CombinedKernels[i];

        rVal += image.r * kernels;
        gVal += image.g * kernels;
        bVal += image.b * kernels;
    }

    OutputR = rVal;
    OutputG = gVal;
    OutputB = bVal;
}
