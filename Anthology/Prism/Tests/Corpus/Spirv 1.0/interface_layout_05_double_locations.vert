#version 450
#extension GL_ARB_gpu_shader_fp64 : require
layout(location=0) in vec4 p;
layout(location=0) out dvec4 d;
layout(location=2) out float tail;
void main(){ d=dvec4(p); tail=p.w; gl_Position=p; }
