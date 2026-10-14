// glslang rejects this shader (assign void call); our compiler must reject it too.
#version 460
void f(){} void main(){ float x = f(); }
