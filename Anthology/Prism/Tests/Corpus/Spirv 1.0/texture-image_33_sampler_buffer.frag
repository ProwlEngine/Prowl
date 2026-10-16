#version 460 core

// samplerBuffer: only texelFetch is defined (no implicit or explicit LOD sampling).
layout(set = 0, binding = 0) uniform samplerBuffer tex;

layout(location = 0) in float u;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = texelFetch(tex, int(u * 256.0));
}
