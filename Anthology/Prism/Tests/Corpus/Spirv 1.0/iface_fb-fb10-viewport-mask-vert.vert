#version 450
#extension GL_NV_viewport_array2 : require
layout(viewport_relative) out highp int gl_Layer;
void main(){ gl_ViewportMask[0] = 1; gl_Layer = 0; gl_Position = vec4(0); }
