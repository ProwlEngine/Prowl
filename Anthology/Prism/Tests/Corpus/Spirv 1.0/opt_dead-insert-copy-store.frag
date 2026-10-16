#version 450
layout(location=0) in vec4 a;
layout(location=0) out vec4 o;
layout(std430, binding=0) buffer B { mat2 r; } b;
void main(){
    mat2 m = mat2(a.xy, a.zw);
    m[0].y = 4.0;
    mat2 n = m;
    n[1].x = 9.0;
    n[1] = vec2(a.x);
    b.r = n;
    o = vec4(m[1], n[0]);
}
