#version 450
layout(constant_id=0) const int N = 4;
layout(std140, binding=0) uniform U { vec4 c[N]; float tail; } u;
layout(location=0) out vec4 o;
void main(){ o = u.c[2] + u.tail; }
