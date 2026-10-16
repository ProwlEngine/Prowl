#version 450
layout(xfb_buffer=1, xfb_stride=32) out;
layout(location=0, xfb_offset=0) out vec4 a;
layout(location=1, xfb_offset=16) out float b;
void main(){ a = vec4(1); b = 2.0; gl_Position = vec4(0); }
