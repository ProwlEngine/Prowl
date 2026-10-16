#version 460 core

// sampler2DMSArray: per-sample texelFetch (needs the ImageMSArray capability).
layout(set = 0, binding = 0) uniform sampler2DMSArray tex;

layout(location = 0) in vec3 uvw;
layout(location = 0) out vec4 outColor;

void main() {
    ivec3 coord = ivec3(uvw.xy * 256.0, uvw.z);
    outColor = texelFetch(tex, coord, 2);
}
