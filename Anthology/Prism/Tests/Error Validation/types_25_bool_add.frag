// glslang rejects this shader (arithmetic on boolean operands); our compiler must reject it too.
#version 460
void main(){ bool[3] b; b[0] = b[0] + b[1]; }
