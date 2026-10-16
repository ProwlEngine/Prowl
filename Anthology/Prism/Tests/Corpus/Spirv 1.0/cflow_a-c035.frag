#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
int c=0; void a(){ c+=1; } void b(){ c+=2; } void main(){ vin.x>0.0 ? a() : b(); o=vec4(c); }
