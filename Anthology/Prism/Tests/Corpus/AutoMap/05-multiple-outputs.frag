#version 450
in vec4 a;
out vec4 c0;
out vec4 c1;
void main(){ c0 = a; c1 = a.wzyx; }
