// glslang rejects this shader (mod float); our compiler must reject it too.
#version 460
void main(){ ivec2 v = ivec2(1.0, 2.0)  0vec2(0); float f=1.0%2.0; }
