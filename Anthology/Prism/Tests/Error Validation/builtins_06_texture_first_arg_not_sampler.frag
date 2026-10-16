// texture called with a float first argument instead of a sampler
#version 450

layout(location=0) out vec4 outColor;

void main() {
    outColor = texture(1.0, vec2(0.0));
}
