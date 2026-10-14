// glslang rejects this shader (imagestore float); our compiler must reject it too.
#version 460
void main(){ float f; imageStore(f, ivec2(0), vec4(0)); }
