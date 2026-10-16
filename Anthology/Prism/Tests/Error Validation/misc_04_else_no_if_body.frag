// glslang rejects this shader (else no if body); our compiler must reject it too.
#version 460
void main(){ if(true) else {} }
