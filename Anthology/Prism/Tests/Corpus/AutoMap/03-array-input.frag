#version 450
in vec4 arr[2];
in float t;
out vec4 o;
void main(){ o = arr[0] + arr[1] + vec4(t); }
