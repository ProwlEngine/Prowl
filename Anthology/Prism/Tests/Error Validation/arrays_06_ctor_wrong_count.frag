// glslang rejects this shader (ctor wrong count); our compiler must reject it too.
#version 460
void main(){ float a[2] = float[](1.0,2.0,3.0); }
