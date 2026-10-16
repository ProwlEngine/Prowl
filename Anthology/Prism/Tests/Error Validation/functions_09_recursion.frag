// glslang rejects this shader (recursion); our compiler must reject it too.
#version 460
int f(int n){ return f(n-1); }
void main(){ f(3); }
