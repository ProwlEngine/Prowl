#version 460

layout(location = 0) in vec2 vCoord;

layout(location = 0) out vec4 fragColor;

void main() {
    vec2 c = interpolateAtSample(vCoord, gl_SampleID);
    fragColor = vec4(c, 0.0, 1.0);
}
