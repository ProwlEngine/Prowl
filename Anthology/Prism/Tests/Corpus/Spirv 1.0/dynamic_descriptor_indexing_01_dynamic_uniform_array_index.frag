#version 460 core

layout(set = 0, binding = 0) uniform sampler2D textures[8];

layout(push_constant) uniform PushConstants {
    int textureIndex;
} pc;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

void main() {
    FragColor = texture(textures[pc.textureIndex], uv);
}
