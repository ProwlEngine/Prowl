#version 450
#extension GL_EXT_control_flow_attributes : require
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(location=2) in vec4 vv;
layout(set=0,binding=0) uniform U { int ui; uint uu; float uf; vec4 uv; int arr[8]; } u;
layout(set=0,binding=1) buffer B { float data[]; } b;

void main(){ float s=0.0; [[unroll]] for(int i=0;i<=4;i+=2){ [[unroll]] for(int j=0;j<i;j++) s+=b.data[i+j]; } o=vec4(s); }
