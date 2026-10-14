// glslang rejects this shader (dup location); our compiler must reject it too.
#version 460
layout(location=0) in vec4 a; layout(location=0) in vec4 b;
void main(){}
