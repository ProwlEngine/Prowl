#version 450
#extension GL_NV_stereo_view_rendering : require
#extension GL_NVX_multiview_per_view_attributes : require
layout(vertices=3) out;
void main(){ gl_out[gl_InvocationID].gl_SecondaryPositionNV = vec4(1); gl_out[gl_InvocationID].gl_PositionPerViewNV[1] = vec4(2); }
