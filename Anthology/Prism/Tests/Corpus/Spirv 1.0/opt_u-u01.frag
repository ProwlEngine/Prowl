#version 450
#extension GL_EXT_control_flow_attributes : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(set=0,binding=1) buffer B { float data[]; } b;

void main(){ float s=0.0; [[unroll]] for(int i=0;i<4;i++){ float t; if(b.data[i]>0.0) t=1.0; else t=2.0; s+=t; } o=vec4(s); }
