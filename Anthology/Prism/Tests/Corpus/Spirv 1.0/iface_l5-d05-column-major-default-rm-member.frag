#version 450
layout(row_major) uniform;
layout(std140, binding=0) uniform U { layout(column_major) mat2x3 a; mat3x2 b; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.a[1], u.b[2].x); }
