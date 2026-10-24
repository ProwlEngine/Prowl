#version 450
layout(set=0, binding=0, r32ui) uniform uimage2D img;
layout(location=0) flat in uint v;
layout(location=0) out uint o;
void main() { o = imageAtomicCompSwap(img, ivec2(0), v, v); }
