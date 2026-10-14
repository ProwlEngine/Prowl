// texture first argument is a vec2 but it must be a sampler
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec2 uv;
void main() {
    outColor = texture(uv, uv);
}
