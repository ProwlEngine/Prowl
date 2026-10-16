// glslang rejects this shader (gl prefix var); our compiler must reject it too.
#version 460
float gl_myvar;
void main(){}
