#version 450
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
void main(){
  float a = uv.x > 0.5 ? uv.y > 0.5 ? 1.0 : 0.5 : 0.0;
  o = vec4(a);
}