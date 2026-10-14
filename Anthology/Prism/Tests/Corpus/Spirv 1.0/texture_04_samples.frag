#version 460

layout(set=0, binding=0) uniform sampler2DMS texMS;

layout(location=0) out vec4 fragColor;

void main() {
    int n = textureSamples(texMS);
    fragColor = vec4(float(n), 0.0, 0.0, 1.0);
}
