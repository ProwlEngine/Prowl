// glslang rejects this shader (assign vec to ivec); our compiler must reject it too.
#version 460
void main(){ ivec3 a; vec3 b; a = b; }
