#version 450
#extension GL_NV_viewport_array2 : require
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
void main(){ gl_ViewportMask[0] = 3; gl_Position = vec4(0); EmitVertex(); }
