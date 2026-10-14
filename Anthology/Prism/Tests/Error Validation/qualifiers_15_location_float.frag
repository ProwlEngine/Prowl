// glslang rejects this shader (location float); our compiler must reject it too.
#version 460
layout(location=0) out vec4 c; layout(location=0.5) out vec4 d;
void main(){}
