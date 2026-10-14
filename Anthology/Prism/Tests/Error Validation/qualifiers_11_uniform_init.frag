// glslang rejects this shader (uniform init); our compiler must reject it too.
#version 460
uniform float u = 1.0;
void main(){}
