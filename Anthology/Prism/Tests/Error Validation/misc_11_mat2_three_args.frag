// glslang rejects this shader (mat2 three args); our compiler must reject it too.
#version 460
void main(){ mat2 m = mat2(1.0,2.0,3.0); }
