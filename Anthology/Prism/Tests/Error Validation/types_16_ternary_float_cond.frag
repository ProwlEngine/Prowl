// glslang rejects this shader (ternary float cond); our compiler must reject it too.
#version 460
void main(){ float f = 1.0 ? 2.0 : 3.0; }
