#version 450
layout(set = 0, binding = 0) uniform sampler2D uTex;
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    ivec2 size = textureSize(uTex, 0);
    ivec2 coord = ivec2(vUV * vec2(size));
    oColor = texelFetch(uTex, coord, 0);
}
