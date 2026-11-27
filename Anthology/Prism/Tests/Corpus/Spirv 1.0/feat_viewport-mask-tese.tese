#version 450
#extension GL_NV_viewport_array2 : require
layout(triangles) in;
layout(viewport_relative) out int gl_Layer;
void main(){ gl_ViewportMask[1] = 2; gl_ViewportMask[0] = 1; gl_Layer = 1; gl_Position = vec4(gl_TessCoord, 1.0); }
