#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
layout(constant_id=1) const int N = 3; float a[N]; void main(){ for(int i=0;i<N;i++) a[i]=float(i); o=vec4(a[ii], a.length(), 0,0); }
