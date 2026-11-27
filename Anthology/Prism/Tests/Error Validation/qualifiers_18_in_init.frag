// glslang rejects this shader (in init); our compiler must reject it too.
#version 460
layout(location=0) in float x = 1.0;
void main(){}
