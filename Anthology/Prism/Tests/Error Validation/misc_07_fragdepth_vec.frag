// glslang rejects this shader (fragdepth vec); our compiler must reject it too.
#version 460
void main(){ gl_FragDepth = vec4(0); }
