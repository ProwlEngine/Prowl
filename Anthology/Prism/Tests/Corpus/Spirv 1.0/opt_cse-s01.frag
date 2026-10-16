#version 450
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;

layout(set=0,binding=0) buffer B { float x; } bs[2]; void main(){ float a = bs[ii].x; bs[ii].x = 5.0; float c = bs[ii].x; o = vec4(a, c, 0, 1); }
