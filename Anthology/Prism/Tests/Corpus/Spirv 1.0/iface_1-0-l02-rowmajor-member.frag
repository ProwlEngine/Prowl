#version 450
layout(std140, binding=0, row_major) uniform U { mat2x3 a; layout(column_major) mat3x2 b; mat4 c[2]; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.a[1], u.b[2].x) + u.c[1][3]; }
