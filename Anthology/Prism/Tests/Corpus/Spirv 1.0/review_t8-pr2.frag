#version 450
layout(location=0) in vec4 v;
layout(location=0) out vec4 c;
struct S { float b; float bc; };
void main(){
  S s;
  s.b = v.x*v.y + v.z;
  s.bc = v.z*v.w + v.x;
  precise float r = s.bc;
  c = vec4(r, s.b, 0, 0);
}
