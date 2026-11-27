#version 460 core

// isamplerCube: integer-texel cube sampling via texture and textureLod (result is ivec4).
layout(set = 0, binding = 0) uniform isamplerCube tex;

layout(location = 0) in vec3 dir;
layout(location = 0) out vec4 outColor;

void main() {
    ivec4 a = texture(tex, dir);
    ivec4 b = textureLod(tex, dir, 1.0);
    outColor = vec4(a + b);
}
