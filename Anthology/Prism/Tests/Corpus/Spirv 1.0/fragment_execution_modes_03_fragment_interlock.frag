#version 450
#extension GL_ARB_fragment_shader_interlock : require
layout(pixel_interlock_ordered) in;
layout(set=0, binding=0, r32ui) uniform uimage2D img;
void main() { beginInvocationInterlockARB(); imageStore(img, ivec2(0), uvec4(1)); endInvocationInterlockARB(); }
