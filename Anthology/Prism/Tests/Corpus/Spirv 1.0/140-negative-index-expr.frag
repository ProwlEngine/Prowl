#version 450
layout(location=0) out vec4 o;
void main(){
  float a[8]; for(int i=0;i<8;i++) a[i]=float(i);
  int i = 3; int j = -i;
  o = vec4(a[abs(j)], a[8-i], a[i+2], a[i*i-i-1]);
}