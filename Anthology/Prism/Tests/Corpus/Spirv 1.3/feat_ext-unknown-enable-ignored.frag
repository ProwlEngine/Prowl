#version 450
#extension GL_FOO_unknown : enable
#extension GL_BAR_unknown : warn
#extension GL_KHR_shader_subgroup_vote : enable
layout(location=0) out vec4 o;
void main(){ o = vec4(subgroupAll(true) ? 1.0 : 0.0); }
