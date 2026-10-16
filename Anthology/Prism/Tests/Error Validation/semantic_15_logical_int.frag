// glslang rejects this shader (logical int); our compiler must reject it too.
#version 460
void main(){ int a=1; if(a && a){} }
