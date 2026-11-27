// glslang rejects this shader (float to bool); our compiler must reject it too.
#version 460
void main(){ float f=1.0; bool b = f; }
