#version 450
layout(set = 0, binding = 0) uniform sampler2D uTex;
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    vec4 c = textureOffset(uTex, vUV, ivec2(1, 1));
    vec4 g = textureGather(uTex, vUV, 0);
    oColor = c + g * 0.25;
}
