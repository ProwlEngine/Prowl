#version 450
layout(location=0) out vec4 o;
void main(){
  int a = 2147483647;
  uint b = 4294967295u;
  int c = -2147483648;
  o = vec4(float(a)+float(b)+float(c));
}