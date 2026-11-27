#version 460 core

layout(location = 0) out vec4 fragColor;

layout(location = 1) in vec3 vSkyColor;
layout(location = 2) in vec3 vDirection;
layout(location = 3) in vec3 vSunDir;

void main()
{
    vec3 color = vSkyColor;
    color.rgb += smoothstep(0.996, 0.9965, dot(normalize(vDirection), vSunDir)); // Sun
    color = 1.0 - exp(-1.0 * color); // Exposure
    fragColor = vec4(color, 1.0);
}
