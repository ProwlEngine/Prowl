// glslang rejects this shader (block init); our compiler must reject it too.
#version 460
layout(set=0,binding=0) uniform B { float a; } b = B(1.0);
void main(){}
