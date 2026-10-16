#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void main(){ dmat2 m = dmat2(1); m++; o = vec4(m[0][0]); }
