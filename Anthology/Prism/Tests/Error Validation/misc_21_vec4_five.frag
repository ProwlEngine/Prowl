// glslang rejects this shader (vec4 five); our compiler must reject it too.
#version 460
void main(){ vec4 v = vec4(1,2,3,4,5); }
