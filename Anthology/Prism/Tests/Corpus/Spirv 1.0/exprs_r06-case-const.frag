#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
const int A = 3; void main(){ float r = 0.0; switch(i){ case A: r = 1.0; break; } o = vec4(r); }
