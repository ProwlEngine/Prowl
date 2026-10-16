// glslang rejects this shader (location negative); our compiler must reject it too.
#version 460
layout(location=-1) out vec4 c;
void main(){}
