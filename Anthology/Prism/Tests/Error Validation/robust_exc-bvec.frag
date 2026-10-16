#version 450
layout(location=0) in float f;
layout(location=1) in vec4 va;
layout(location=0) out vec4 o;

void main(){ bvec4 b = bvec4(va, va); o = vec4(b); }
