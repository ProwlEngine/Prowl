#version 450
layout(location=0) out vec4 o;
#ifdef VULKAN
void main(){ o = vec4(1); }
#else
void main(){ o = vec4(2); }
#endif
