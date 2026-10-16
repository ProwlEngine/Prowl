// glslang rejects this shader (length no parens); our compiler must reject it too.
#version 460
void main(){ int a[3]; int n = a.length; }
