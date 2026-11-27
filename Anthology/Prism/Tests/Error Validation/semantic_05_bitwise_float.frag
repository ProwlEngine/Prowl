// glslang rejects this shader (bitwise float); our compiler must reject it too.
#version 460
void main(){ bool b=true; float f = 1.0 & 2.0; }
