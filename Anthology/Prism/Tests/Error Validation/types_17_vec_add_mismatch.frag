// glslang rejects this shader (vec add mismatch); our compiler must reject it too.
#version 460
void main(){ vec3 a=vec3(0); vec2 b=vec2(0); vec3 c = a+b; }
