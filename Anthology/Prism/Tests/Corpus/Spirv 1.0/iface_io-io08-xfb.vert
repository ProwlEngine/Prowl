#version 450
layout(xfb_buffer=0, xfb_stride=32) out;
layout(location=0, xfb_offset=0) out vec4 a;
layout(location=1, xfb_buffer=1, xfb_offset=4) out float b;
layout(xfb_buffer=0) out gl_PerVertex { layout(xfb_offset=16) vec4 gl_Position; };
void main(){ a = vec4(1); b = 2.0; gl_Position = vec4(0); }
