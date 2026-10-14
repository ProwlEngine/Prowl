// glslang rejects this shader (case outside switch); our compiler must reject it too.
#version 460
void main(){ for(int i=0;i<3;i++) case 1: ; }
