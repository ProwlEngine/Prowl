#version 450
layout(std140, binding=0, align=64) uniform U { float a; vec4 x; float b; } u;
layout(location=0) out vec4 c;
void main(){ c = u.x+u.a+u.b; }
