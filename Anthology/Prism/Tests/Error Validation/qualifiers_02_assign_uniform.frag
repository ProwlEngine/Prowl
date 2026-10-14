// glslang rejects this shader (assign uniform); our compiler must reject it too.
#version 460
uniform float u; void main(){ u = 1.0; }
