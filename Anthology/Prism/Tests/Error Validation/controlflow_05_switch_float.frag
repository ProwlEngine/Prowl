// glslang rejects this shader (switch float); our compiler must reject it too.
#version 460
void main(){ switch(1.0){ case 1: break; } }
