#version 450
layout(location=0) out vec4 o;
void main(){
  float acc = 0.0; int i = 0;
  do {
    i++;
    if (i == 3) continue;
    if (i > 8) break;
    acc += float(i);
  } while (i < 10);
  o = vec4(acc, float(i), 0, 1);
}