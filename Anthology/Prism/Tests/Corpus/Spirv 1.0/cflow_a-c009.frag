#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void main(){ vec4 a=vec4(0); switch(ii){ case -1: a.x=1.0; break; case -2147483648: a.y=1.0; break; case 1+2*3: a.z=1.0; break; } o=a; }
