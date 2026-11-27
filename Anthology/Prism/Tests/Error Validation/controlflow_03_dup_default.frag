// glslang rejects this shader (dup default); our compiler must reject it too.
#version 460
void main(){ int x=1; switch(x){ default: break; default: break; } }
