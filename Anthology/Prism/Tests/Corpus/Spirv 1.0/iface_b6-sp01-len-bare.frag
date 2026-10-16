#version 450
layout(constant_id=0) const int N = 4;
layout(location=0) out vec4 o;
void main(){ float a[N]; for(int i=0;i<a.length();i++) a[i]=float(i); o = vec4(a[1], a.length(), 0, 0); }
