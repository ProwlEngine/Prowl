#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
void main(){ mat2 a = mat2(1); mat3 b = mat3(1); mat2 c = a + b; o = vec4(c[0], 0, 0); }
