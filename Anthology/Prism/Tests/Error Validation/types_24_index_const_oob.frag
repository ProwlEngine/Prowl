// glslang rejects this shader (constant array index out of range); our compiler must reject it too.
#version 460
layout(location=0) out vec4 o; void main(){ int a[2]; o = vec4(a[5]); }
