#version 450
layout(location=0) in float f;
layout(location=1) in vec4 va;
layout(location=0) out vec4 o;

void main(){ bvec2 b = bvec2(va.xy, f); o = vec4(b, b); }
