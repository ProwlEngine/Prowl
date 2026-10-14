// glslang rejects this shader (color swizzle oob); our compiler must reject it too.
#version 460
void main(){ float f = vec3(1.0).a; }
