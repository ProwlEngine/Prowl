#version 450
in vec3 a;
in vec2 b;
out vec4 o;
void main(){ o = vec4(a, b.x); }
