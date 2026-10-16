// glslang rejects this shader (const uniform); our compiler must reject it too.
#version 460
const uniform float u;
void main(){}
