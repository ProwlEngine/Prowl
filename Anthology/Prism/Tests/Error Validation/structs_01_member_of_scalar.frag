// glslang rejects this shader (member of scalar); our compiler must reject it too.
#version 460
void main(){ struct S{float a;} s; float f = s.a.x.y; }
