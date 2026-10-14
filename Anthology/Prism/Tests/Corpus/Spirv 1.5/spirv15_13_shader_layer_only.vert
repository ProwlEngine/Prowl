#version 450
#extension GL_ARB_shader_viewport_layer_array : require
layout(location = 0) in vec4 p;
void main() { gl_Position = p; gl_Layer = 1; }
