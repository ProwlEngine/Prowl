#version 450
#extension GL_EXT_control_flow_attributes : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(set=0,binding=1) buffer B { float data[]; } b;

void main(){ float s=0.0; int j=0; [[unroll]] for(int i=0;i<4;i=1+j){ j=i+2; s+=b.data[i]; } o=vec4(s); }
