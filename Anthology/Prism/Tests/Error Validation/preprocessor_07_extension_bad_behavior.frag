// glslang rejects this shader (extension bad behavior); our compiler must reject it too.
#version 460
#extension GL_ARB_shading_language_420pack : banana
void main(){}
