// glslang rejects this shader (while float); our compiler must reject it too.
#version 460
void main(){ while(1.0){} }
