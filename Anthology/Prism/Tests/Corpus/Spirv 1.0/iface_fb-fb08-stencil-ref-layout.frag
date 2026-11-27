#version 450
#extension GL_ARB_shader_stencil_export : require
#extension GL_AMD_shader_early_and_late_fragment_tests : require
layout(stencil_ref_greater_front_amd) out int gl_FragStencilRefARB;
void main(){ gl_FragStencilRefARB = 1; }
