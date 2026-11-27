#version 450
layout(location=0) out vec4 o;
#extension GL_EXT_control_flow_attributes : enable
layout(location=1) flat in int ii;
void main(){ [[flatten]] switch(ii){ case 0: o = vec4(1); break; default: o = vec4(2); } }
