#version 450
layout(std140,set=0,binding=0) uniform U {
  dmat2 a;
  dmat3 b;
  dmat4 c;
  dmat2x3 d;
  dmat4x2 e;
} u;
layout(location=0) out vec4 o;
void main() { o = vec4(float(u.a[0][0] + u.b[1][1] + u.c[2][2] + u.d[1][2] + u.e[3][1])); }
