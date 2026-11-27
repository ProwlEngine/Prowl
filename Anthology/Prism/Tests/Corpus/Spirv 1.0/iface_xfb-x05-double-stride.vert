#version 450
layout(location=0, xfb_offset=0) out double d;
layout(location=1, xfb_offset=8) out float f;
void main(){ d = 1.0; f = 2.0; gl_Position = vec4(0); }
