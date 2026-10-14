#version 460

layout(location = 0) in vec2 inUV;
layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    vec2 offset;
    int sampleId;
} u;

void main() {
    vec2 c = interpolateAtCentroid(inUV);
    vec2 o = interpolateAtOffset(inUV, u.offset);
    vec2 s = interpolateAtSample(inUV, u.sampleId);
    fragColor = vec4(c, o.x, s.y);
}
