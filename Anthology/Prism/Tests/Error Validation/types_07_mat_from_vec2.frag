// glslang rejects this shader (mat from vec2); our compiler must reject it too.
#version 460
void main(){ mat3 m = mat3(vec2(0)); }
