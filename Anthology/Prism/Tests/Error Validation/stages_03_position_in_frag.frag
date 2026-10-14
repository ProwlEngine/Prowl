// glslang rejects this shader (position in frag); our compiler must reject it too.
#version 460
void main(){ gl_Position = vec4(0); }
