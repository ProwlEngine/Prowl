// glslang rejects this shader (location local); our compiler must reject it too.
#version 460
void main(){ layout(location=0) float x; }
