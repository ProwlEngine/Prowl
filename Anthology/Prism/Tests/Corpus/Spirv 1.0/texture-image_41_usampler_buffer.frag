#version 460 core

// usamplerBuffer: only texelFetch is defined (result is uvec4).
layout(set = 0, binding = 0) uniform usamplerBuffer tex;

layout(location = 0) in float u;
layout(location = 0) out vec4 outColor;

void main() {
    uvec4 texel = texelFetch(tex, int(u * 256.0));
    outColor = vec4(texel);
}
