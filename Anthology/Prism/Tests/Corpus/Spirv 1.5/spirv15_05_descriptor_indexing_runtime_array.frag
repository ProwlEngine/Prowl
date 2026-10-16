#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(set = 0, binding = 0) uniform sampler2D tex[];
layout(push_constant) uniform P { int i; } pc;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 o;
void main() { o = texture(tex[pc.i], uv); }
