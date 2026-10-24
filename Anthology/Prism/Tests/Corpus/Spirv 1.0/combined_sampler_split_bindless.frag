#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(set=0, binding=0) uniform texture2D texs[];
layout(set=0, binding=1) uniform sampler smps[4];
layout(location=0) flat in uint idx;
layout(location=1) in vec2 uv;
layout(location=0) out vec4 o;
vec4 Read(sampler2D s, vec2 p) { return texture(s, p); }
vec4 Read_split0(texture2D s_texture, sampler s_sampler, vec2 p) { return texture(sampler2D(nonuniformEXT(s_texture), nonuniformEXT(s_sampler)), p); }
void main() { o = Read_split0(texs[nonuniformEXT(idx)], smps[nonuniformEXT(idx & 3u)], uv); }
