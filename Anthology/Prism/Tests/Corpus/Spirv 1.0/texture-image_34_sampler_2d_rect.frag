#version 460 core

// sampler2DRect: unnormalized-coordinate sampling via texture and texelFetch (no LOD).
layout(set = 0, binding = 0) uniform sampler2DRect tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 a = texture(tex, uv);
    vec4 b = texelFetch(tex, ivec2(uv));
    outColor = a + b;
}
