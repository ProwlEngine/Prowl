#version 450
#extension GL_ARB_shader_ballot : require
#extension GL_ARB_gpu_shader_int64 : require
layout(location=0) out vec4 o;
void main(){ uint64_t m = gl_SubGroupLtMaskARB | gl_SubGroupGeMaskARB; uint64_t g = gl_SubGroupGtMaskARB ^ gl_SubGroupLeMaskARB; o = vec4(float(gl_SubGroupSizeARB), float(gl_SubGroupInvocationARB), float(m & 1ul), float(g >> 3)); }
