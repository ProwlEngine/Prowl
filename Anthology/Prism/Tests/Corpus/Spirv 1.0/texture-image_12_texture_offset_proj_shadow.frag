#version 460 core

// textureOffset and textureProj on sampler2DShadow: must route through OpImageSample*Dref* and return a scalar.
layout(set = 0, binding = 0) uniform sampler2DShadow tex;

layout(location = 0) in vec3 uvw;
layout(location = 0) out vec4 outColor;

void main() {
    float a = textureOffset(tex, uvw, ivec2(1, 1));
    float b = textureProj(tex, vec4(uvw, 2.0));
    outColor = vec4(a, b, a * b, 1.0);
}
