#version 450
layout(location=0) in vec4 p;
layout(location=0, set=1) out vec4 c;
void main(){ c = p; gl_Position = p; }
