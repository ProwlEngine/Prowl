#version 450
layout(std140, binding=0) uniform U { vec4 a; } u[2][3];
layout(location=0) out vec4 c;
void main(){ c = u[1][2].a; }
