// glslang rejects this shader (not float); our compiler must reject it too.
#version 460
void main(){ bool b = !1.0; }
