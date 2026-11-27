// glslang rejects this shader (compound assign mismatch); our compiler must reject it too.
#version 460
void main(){ int i; i += vec2(0); }
