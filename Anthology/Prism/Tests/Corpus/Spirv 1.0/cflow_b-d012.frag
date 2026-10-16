#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void main(){ int i = 5; for(int i = i+1; i<10; i++) o.x+=1.0; }
