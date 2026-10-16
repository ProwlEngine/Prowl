#version 450
layout(location=0) in float a;
layout(location=0) out vec4 o;
void main() {
  float s = 0.0;
  if (a > 0.0) { } else { }
  if (a > 1.0) { }
  if (a > 2.0) { } else { s += 1.0; }
  if (a > 3.0) { s += 2.0; } else { }
  o = vec4(s);
}
