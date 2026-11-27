#version 450
#extension GL_NVX_multiview_per_view_attributes : require
layout(vertices=3) out;
void main(){ gl_PositionPerViewNV[0] = vec4(1); }
