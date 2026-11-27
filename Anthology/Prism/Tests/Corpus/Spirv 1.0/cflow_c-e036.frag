#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void main(){ mat2 m = mat2(vin); m++; m[0]--; o=vec4(m[0],m[1]); }
