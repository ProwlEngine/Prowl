#version 450
layout(local_size_x=8) in;
layout(location=0) out vec4 o;
void main() { o = vec4(1.0); }
