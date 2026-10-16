#version 450
#extension GL_ARB_shader_stencil_export : require
layout(location=0) out vec4 o;
layout(stencil_ref_greater_front_amd, location=1) out int s;
void main(){ o = vec4(1); s = 1; }
