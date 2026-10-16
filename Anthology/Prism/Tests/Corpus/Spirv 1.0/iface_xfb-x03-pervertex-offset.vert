#version 450
out gl_PerVertex { layout(xfb_buffer=0, xfb_offset=0) vec4 gl_Position; };
void main(){ gl_Position = vec4(0); }
