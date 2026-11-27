#version 450
layout(constant_id=0) const int N = 4;
layout(location=0) out vec4 o;
void main(){ float a[N+1]; a[0]=1.0; o = vec4(a[0], a.length(), 0, 0); }
