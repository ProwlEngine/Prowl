#version 450
#extension GL_NV_viewport_array2 : require
#extension GL_NV_stereo_view_rendering : require
layout(viewport_relative, secondary_view_offset = 2) out highp int gl_Layer;
void main(){ gl_Layer = 3; gl_Position = vec4(1.0); }
