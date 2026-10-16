#version 460
#extension GL_EXT_ray_tracing : require
#extension GL_EXT_ray_cull_mask : require
layout(location=0) rayPayloadInEXT uint p;
void main(){ p = gl_CullMaskEXT & 0x0Fu; }
