#version 450
layout(location=0) in float f;
layout(location=0) out vec4 o;
layout(std140, binding=0) uniform U { mat4 a[2147483647]; float b; } u;
void main(){ o = vec4(u.b); }
