#version 450
layout(std140,set=0,binding=0) uniform U {
  layout(row_major) dmat3 a;
  layout(column_major) dmat3 b;
  layout(row_major) mat2x4 c;
} u;
layout(location=0) out vec4 o;
void main() { o = vec4(float(u.a[0][0] + u.b[1][1]) + u.c[1][3]); }
