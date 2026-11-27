// glslang rejects this shader (pow mismatch); our compiler must reject it too.
#version 460
void main(){ float f = pow(vec2(1.0), vec3(1.0)); }
