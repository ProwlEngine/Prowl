#version 450
layout(location=0) in float f;
layout(location=1) in vec4 va;
layout(location=0) out vec4 o;

void main(){ float r = isinf() ? 1.0 : 0.0; o = vec4(r); }
