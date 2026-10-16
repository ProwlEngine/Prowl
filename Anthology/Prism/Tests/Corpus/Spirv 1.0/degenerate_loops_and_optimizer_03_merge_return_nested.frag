#version 450
layout(location=0) in float a;
layout(location=0) out vec4 o;
float f(float x) {
  if (x < 0.0) return -1.0;
  for (int i = 0; i < 4; ++i) {
    if (x == float(i)) {
      switch (i) { case 0: return 10.0; case 1: break; default: return 20.0; }
      while (x > 0.0) { if (x > 5.0) return 30.0; break; }
      return 40.0;
    }
  }
  if (x > 100.0) { if (x > 200.0) return 50.0; return 60.0; }
  return 70.0;
}
void main() { o = vec4(f(a)); }
