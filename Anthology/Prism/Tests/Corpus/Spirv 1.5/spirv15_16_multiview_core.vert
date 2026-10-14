#version 450
#extension GL_EXT_multiview : require
void main() { gl_Position = vec4(float(gl_ViewIndex)); }
