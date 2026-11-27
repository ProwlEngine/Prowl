// glslang rejects this shader (number as lvalue); our compiler must reject it too.
#version 460
void main(){ float 3 = 4.0; }
