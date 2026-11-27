#version 450
layout(location=0) in flat int mode;
layout(location=0) out vec4 o;
void main(){
  float f = 0.0;
  switch (mode) {
    case 0: f = 1.0; break;
    case 1: case 2: f = 2.0; break;
    case 3: f = 3.0; // fallthrough (no break)
    default: f = 4.0; break;
  }
  o = vec4(f);
}