// glslang rejects this shader (ambiguous overload); our compiler must reject it too.
#version 460
void f(); void f(int a);
void main(){ f(1.0); }
