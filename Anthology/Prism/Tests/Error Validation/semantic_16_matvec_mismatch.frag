// glslang rejects this shader (matvec mismatch); our compiler must reject it too.
#version 460
void main(){ mat2 m; vec3 v; vec3 r = m*v; }
