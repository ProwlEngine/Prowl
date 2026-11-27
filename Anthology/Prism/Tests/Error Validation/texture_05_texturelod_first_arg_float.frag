// textureLod first argument is a float but it must be a sampler
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec2 uv;
void main() {
    outColor = textureLod(1.0, uv, 0.0);
}
