#version 450
layout(location=0) out vec4 c;
void main(){
#ifdef VULKAN
  c = vec4(VULKAN);
#else
  c = vec4(0.5);
#endif
}
