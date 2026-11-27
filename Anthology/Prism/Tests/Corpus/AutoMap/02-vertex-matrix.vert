#version 450
in vec2 a;
in mat3 m;
in float f;
out vec3 o;
void main(){ gl_Position = vec4(a, f, 1.0); o = m[0]; }
