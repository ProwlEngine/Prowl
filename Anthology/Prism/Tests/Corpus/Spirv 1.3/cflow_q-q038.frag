#version 450
layout(location=0) out vec4 o;
#extension GL_EXT_control_flow_attributes : enable
void main(){ [[loop]] for(int i=0;i<4;i++) o.x += 1.0; [[dependency_infinite]] for(int i=0;i<4;i++) o.y += 1.0; [[dependency_length(2)]] for(int i=0;i<4;i++) o.z += 1.0; }
