// glslang rejects this shader (call undeclared); our compiler must reject it too.
#version 460
void main(){ nofunc(1.0); }
