#version 460 core

// sampler1DShadow: depth-compare sampling returns a scalar (texture and textureLod).
layout(set = 0, binding = 0) uniform sampler1DShadow tex;

layout(location = 0) in vec3 uvw;
layout(location = 0) out vec4 outColor;

void main() {
    float a = texture(tex, uvw);
    float b = textureLod(tex, uvw, 1.0);
    outColor = vec4(a, b, a * b, 1.0);
}
