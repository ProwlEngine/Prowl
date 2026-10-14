#version 450
layout(location=0) in vec4 v;
layout(location=0) out vec4 o;
void main() {
  o = vec4(dFdx(v.x), dFdy(v.y), fwidth(v.z), dFdxFine(v.w))
    + vec4(dFdxCoarse(v.x), dFdyFine(v.y), dFdyCoarse(v.z), fwidthFine(v.w))
    + vec4(fwidthCoarse(v.x), 0.0, 0.0, 0.0);
}
