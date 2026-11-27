#version 450
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;

layout(set=0,binding=0) buffer B { float x[4]; } bs[]; void main(){ float a = bs[ii].x[0]; bs[ii].x[0] = 5.0; float c = bs[ii].x[0]; o = vec4(a, c, 0, 1); }
