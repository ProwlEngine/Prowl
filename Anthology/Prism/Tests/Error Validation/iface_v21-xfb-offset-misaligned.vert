#version 450
layout(location=0, xfb_offset=2) out float a;
void main(){ a = 0.0; gl_Position = vec4(0); }
