// glslang rejects this shader (if int); our compiler must reject it too.
#version 460
void main(){ if(1){} }
