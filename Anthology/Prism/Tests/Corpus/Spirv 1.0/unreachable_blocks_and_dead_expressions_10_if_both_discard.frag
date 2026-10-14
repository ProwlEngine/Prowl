#version 450
layout(location=0) out vec4 c;
layout(std430,set=0,binding=0) buffer B { int k; };
void main(){ if(k>0){ discard; } else { discard; } c=vec4(1.0); }
