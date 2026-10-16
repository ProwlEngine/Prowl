#version 450
layout(location=0) flat in int i;
layout(location=0) out float o;
void main(){ o = float[3](1.0, 2.0, 3.0)[i] + float[](4.0, 5.0)[1]; }
