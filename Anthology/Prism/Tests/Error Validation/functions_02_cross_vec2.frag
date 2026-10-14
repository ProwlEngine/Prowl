// glslang rejects this shader (cross vec2); our compiler must reject it too.
#version 460
void main(){ vec3 c = cross(vec2(0), vec2(0)); }
