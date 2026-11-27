// glslang rejects this shader (return value in void); our compiler must reject it too.
#version 460
void f(){ return 1.0; }
void main(){ f(); }
