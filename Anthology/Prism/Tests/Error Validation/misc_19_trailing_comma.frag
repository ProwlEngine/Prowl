// glslang rejects this shader (trailing comma); our compiler must reject it too.
#version 460
void main(){ vec2 v = vec2(1.0,); }
