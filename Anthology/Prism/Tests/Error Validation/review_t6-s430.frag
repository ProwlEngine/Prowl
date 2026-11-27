#version 450
layout(std430, binding=0) uniform U { float a[2]; } u;
layout(location=0) out vec4 c;
void main(){ c = vec4(u.a[1]); }
