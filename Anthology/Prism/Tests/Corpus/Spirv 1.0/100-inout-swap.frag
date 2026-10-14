#version 450
layout(location=0) out vec4 o;
void Swap(inout float a, inout float b){ float t = a; a = b; b = t; }
void Modify(out float a, float v){ a = v * 2.0; }
void main(){
  float x = 1.0, y = 2.0;
  Swap(x, y);
  Modify(x, y);
  o = vec4(x, y, 0, 1);
}