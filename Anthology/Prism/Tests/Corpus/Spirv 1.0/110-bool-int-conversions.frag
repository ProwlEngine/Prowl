#version 450
layout(location=0) out vec4 o;
void main(){
  bool b = true;
  float f = float(b);
  int i = int(b);
  uint u = uint(!b);
  o = vec4(f, float(i), float(u), 1.0);
}