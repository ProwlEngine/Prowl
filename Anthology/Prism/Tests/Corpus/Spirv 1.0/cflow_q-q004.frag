#version 450
layout(location=0) out vec4 o;
#define CAT(a,b) a##b
#define X 1
void main(){ float X1 = 4.0; float y1 = CAT(X,1); o = vec4(y1); }
