// glslang rejects this shader (matrix index oob); our compiler must reject it too.
#version 460
void main(){ mat2 m; m[3]; }
