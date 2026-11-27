#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(set = 0, binding = 0) uniform sampler2D tex[];
layout(location = 0) flat in int idx;
layout(location = 1) in vec2 uv;
layout(location = 0) out vec4 o;
void main() { o = texture(tex[nonuniformEXT(idx)], uv); }
