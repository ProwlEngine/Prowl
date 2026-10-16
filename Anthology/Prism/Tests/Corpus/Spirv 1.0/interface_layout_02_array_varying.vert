#version 450
layout(location=0) in vec4 a;
layout(location=0) out vec3 arr[3];
layout(location=3) out float tail;
void main(){ arr[0]=a.xyz; arr[1]=a.xyz; arr[2]=a.xyz; tail=a.w; gl_Position=a; }
