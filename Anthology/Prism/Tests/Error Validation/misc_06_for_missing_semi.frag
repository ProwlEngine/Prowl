// glslang rejects this shader (for missing semi); our compiler must reject it too.
#version 460
void main(){ for(int i=0 i<3; i++){} }
