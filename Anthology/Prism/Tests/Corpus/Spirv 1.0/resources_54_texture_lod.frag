#version 450
layout(set = 0, binding = 0) uniform sampler2D uTex;
layout(location = 0) in vec2 vUV;
layout(location = 1) in float vLod;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = textureLod(uTex, vUV, vLod);
}
