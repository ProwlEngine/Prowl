#version 450
#extension GL_NV_viewport_array2 : require
out int gl_Layer;
void main(){ gl_Layer = 0; gl_Position = vec4(0); }
