#version 450
layout(location=0) out vec4 o;
void main(){
  vec4 v = vec4(1);
  vec4 w = v.xxxx.xxxx.xxxx.xxxx;
  o = w;
}