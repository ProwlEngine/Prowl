#version 450
layout(location=2) in vec4 x;
in vec2 y;
in vec3 z;
out vec4 o;
void main(){ o = x + vec4(y, z.x, z.y); }
