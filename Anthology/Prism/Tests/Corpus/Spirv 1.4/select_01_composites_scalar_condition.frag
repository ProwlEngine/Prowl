#version 450
struct S { vec2 a; float b; };
layout(location = 0) flat in int c;
layout(location = 0) out vec4 o;
void main() {
  mat2 m1 = mat2(1.0), m2 = mat2(2.0); float a1[2] = float[](1.0, 2.0), a2[2] = float[](3.0, 4.0);
  S s1 = S(vec2(1.0), 2.0), s2 = S(vec2(3.0), 4.0);
  mat2 m = c == 0 ? m1 : m2; float a[2] = c == 1 ? a1 : a2; S s = c == 2 ? s1 : s2;
  vec3 v = c == 3 ? vec3(1.0) : vec3(2.0);
  o = vec4(m[0], a[1], s.b) + v.xyzz;
}
