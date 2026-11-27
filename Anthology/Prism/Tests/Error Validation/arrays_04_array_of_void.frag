// glslang rejects this shader (array of void); our compiler must reject it too.
#version 460
void main(){ void a[3]; }
