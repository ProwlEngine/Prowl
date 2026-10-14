#version 450
#define ID(x) x
#define ADD(a,b) ((a)+(b))
#define TWICE(x) ADD(x,x)
#define NEST(x) TWICE(ID(x))
layout(location=0) out vec4 o;
void main() { o = vec4(TWICE(ADD(1.0,2.0))); }
