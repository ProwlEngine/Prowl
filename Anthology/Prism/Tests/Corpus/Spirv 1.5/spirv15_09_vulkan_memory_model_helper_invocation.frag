#version 450
#pragma use_vulkan_memory_model
layout(location = 0) out vec4 o;
void main() { o = vec4(gl_HelperInvocation ? 1.0 : 0.0); }
