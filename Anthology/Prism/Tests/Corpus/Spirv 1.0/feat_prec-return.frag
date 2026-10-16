#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in double dv;
mediump float half1(float x) { return x * 0.5; }
mediump float half2(double y) { return float(y); }
float full(mediump float x) { return x + 1.0; }
mediump float konst() { return 3.0; }
void main(){ o = vec4(half1(v.x), half2(dv), full(v.y), konst()); }
