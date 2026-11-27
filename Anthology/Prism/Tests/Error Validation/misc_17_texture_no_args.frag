// glslang rejects this shader (texture no args); our compiler must reject it too.
#version 460
layout(location=0) out vec4 o; void main(){ o = vec4(texture()); }
