// glslang rejects this shader (flat out frag); our compiler must reject it too.
#version 460
flat out vec4 c;
void main(){}
