#version 450
layout(location=0) out vec4 o;
void main(){
  float a[3][4];
  for (int i=0;i<3;i++) for (int j=0;j<4;j++) a[i][j] = float(i*4+j);
  o = vec4(a[1][2], a[2][3], a[0][0], a[1][1]);
}