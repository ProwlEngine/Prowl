#version 450
layout(binding=0, r32ui) uniform uimage2DMS ms;
layout(location=0) out vec4 o;
void main(){ o = vec4(imageAtomicAdd(ms, ivec2(0), 2, 1u) + imageAtomicCompSwap(ms, ivec2(1), 1, 3u, 4u)); }
