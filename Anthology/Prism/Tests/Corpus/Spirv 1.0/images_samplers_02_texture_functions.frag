#version 460 core

// texture, textureProj, textureLod, textureGrad, textureOffset
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D tex;

void main() {
    vec4 a = texture(tex, vec2(0.5, 0.5));
    vec4 b = textureProj(tex, vec3(0.5, 0.5, 1.0));
    vec4 c = textureLod(tex, vec2(0.5, 0.5), 2.0);

    vec2 dpdx = vec2(0.01, 0.0);
    vec2 dpdy = vec2(0.0, 0.01);
    vec4 d = textureGrad(tex, vec2(0.5, 0.5), dpdx, dpdy);

    vec4 e = textureOffset(tex, vec2(0.5, 0.5), ivec2(1, -1));

    FragColor = a + b + c + d + e;
}
