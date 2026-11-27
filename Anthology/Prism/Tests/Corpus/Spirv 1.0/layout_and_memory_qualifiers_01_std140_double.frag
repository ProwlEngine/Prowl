#version 450
layout(std140,set=0,binding=0) uniform U {
  float a;
  double b;
  vec2 c;
  dvec2 d;
  float e;
  dvec3 f;
  double g[2];
} u;
layout(location=0) out vec4 o;
void main() { o = vec4(u.a + float(u.b) + u.c.x + float(u.d.y) + u.e + float(u.f.z) + float(u.g[1])); }
