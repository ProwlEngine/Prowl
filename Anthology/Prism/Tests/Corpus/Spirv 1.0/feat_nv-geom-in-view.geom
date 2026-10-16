#version 450
#extension GL_NV_viewport_array2 : require
#extension GL_NV_stereo_view_rendering : require
#extension GL_NVX_multiview_per_view_attributes : require
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
void main(){ gl_Position = gl_in[0].gl_SecondaryPositionNV + gl_in[2].gl_PositionPerViewNV[1]; EmitVertex(); }
