#version 450
layout(location=0) in float a;
layout(location=0) out vec4 o;
float dead(float x) { return x * 2.0; }
void main() {
  float unused = dead(a);
  if (false) { o = vec4(9.0); return; }
  if (true) { o = vec4(a); } else { o = vec4(1.0); discard; }
  for (int i = 0; i < 0; ++i) { o += vec4(5.0); }
  while (false) { o += vec4(6.0); }
  switch (0) { case 1: o += vec4(7.0); break; default: break; }
  return;
}
