#version 460 core

layout(location = 0) in vec2 uv;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D sceneColor;

layout(set = 0, binding = 1) uniform ThresholdParams
{
    float threshold;
    float knee;
} params;

void main()
{
    vec3 color = texture(sceneColor, uv).rgb;
    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));

    float epsilon = 1.0e-4;
    float soft = clamp(luminance - params.threshold + params.knee, 0.0, 2.0 * params.knee);
    soft = soft * soft / (4.0 * params.knee + epsilon);

    float contribution = max(soft, luminance - params.threshold) / max(luminance, epsilon);

    outColor = vec4(color * contribution, 1.0);
}
