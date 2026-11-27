// glslang rejects this shader (ctor too few); our compiler must reject it too.
#version 460
void main(){ vec4 v = vec4(1.0,2.0); }
