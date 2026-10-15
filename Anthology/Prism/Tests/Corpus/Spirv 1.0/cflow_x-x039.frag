#version 450
layout(location=0) out vec4 o;
#define F(x) x
#define G F
void main(){ o=vec4(G(1.0)); }