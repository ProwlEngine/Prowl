#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void main(){ switch(ii){ case 0: return; o.x=1.0; default: o.y=1.0; } }
