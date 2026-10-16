#version 450
layout(location=0) out vec4 o;
#extension GL_EXT_control_flow_attributes : enable
void main(){ [[unroll, dependency_infinite]] for(int i=0;i<4;i++) o.x += 1.0; [[min_iterations(2), max_iterations(8), iteration_multiple(2), peeled_count(1), partial_count(2)]] for(int i=0;i<4;i++) o.y += 1.0; }
