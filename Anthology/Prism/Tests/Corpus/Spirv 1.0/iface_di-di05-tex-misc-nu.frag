#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
layout(binding=0) uniform sampler2D t[];
void main(){ o = textureLod(t[nonuniformEXT(i)], vec2(0), 0.0) + texelFetch(t[nonuniformEXT(i)], ivec2(0), 0) + vec4(textureSize(t[nonuniformEXT(i)], 0), 0, 0); }
