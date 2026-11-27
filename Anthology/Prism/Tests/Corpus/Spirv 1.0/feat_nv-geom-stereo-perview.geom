#version 450
#extension GL_NV_viewport_array2 : require
#extension GL_NV_stereo_view_rendering : require
#extension GL_NVX_multiview_per_view_attributes : require
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
void main(){ gl_Position = vec4(0); gl_SecondaryPositionNV = vec4(1); gl_PositionPerViewNV[0] = vec4(3); EmitVertex(); }
