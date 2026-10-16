#version 450
#extension GL_ARB_shader_stencil_export : require
#extension GL_AMD_shader_early_and_late_fragment_tests : require
layout(early_and_late_fragment_tests_amd) in;
layout(stencil_ref_less_back_amd) out int gl_FragStencilRefARB;
layout(location=0) flat in int s;
void main(){ gl_FragStencilRefARB = s - 1; }
