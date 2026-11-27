#version 450
layout(location=0) in B { vec4 a; } b;
void main(){ gl_Position = b.a; }
