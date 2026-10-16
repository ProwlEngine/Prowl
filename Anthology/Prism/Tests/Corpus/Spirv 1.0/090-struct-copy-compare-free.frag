#version 450
layout(location=0) out vec4 o;
struct Light { vec3 pos; float intensity; };
void main(){
  Light a = Light(vec3(1,2,3), 4.0);
  Light b = a; // whole-aggregate copy
  b.intensity = 5.0;
  bool same = a.pos == b.pos;
  o = vec4(b.pos, (same ? 1.0 : 0.0) * b.intensity);
}