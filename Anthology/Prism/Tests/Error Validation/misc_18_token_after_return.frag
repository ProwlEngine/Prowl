// glslang rejects this shader (token after return); our compiler must reject it too.
#version 460
void main(){ return; extra; }
