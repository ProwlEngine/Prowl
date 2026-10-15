#version 450
#extension GL_KHR_shader_subgroup_basic : require
layout(vertices=1) out;
void main(){ gl_TessLevelOuter[0] = float(gl_SubgroupInvocationID); subgroupBarrier(); }
