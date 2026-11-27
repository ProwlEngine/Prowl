#version 450
layout(location=0, xfb_buffer=0, xfb_offset=0) out dvec3 a;
layout(location=2, xfb_buffer=0, xfb_offset=24) out float b;
void main(){ a = dvec3(1); b = 2.0; gl_Position = vec4(0); }
