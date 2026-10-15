#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
layout(binding=0, r32ui) uniform uimage2D im[];
void main(){ o = vec4(imageAtomicAdd(im[nonuniformEXT(i)], ivec2(0), 1u)) + vec4(imageSize(im[nonuniformEXT(i)]), 0, 0); }
