#version 450
layout(location=0) in vec4 p;
layout(xfb_buffer=0, xfb_offset=0, location=0) out vec4 c;
void main(){ c=p; gl_Position=p; }
