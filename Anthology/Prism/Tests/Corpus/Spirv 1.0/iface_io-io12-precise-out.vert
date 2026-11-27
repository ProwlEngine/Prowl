#version 450
layout(location=0) in vec4 p;
precise gl_Position;
void main(){ gl_Position = p * p + p; }
