#version 450
layout(location=0) in vec4 a;
layout(location=1) flat in int k;
layout(location=0) out vec4 o;
void main(){
    mat4 m = mat4(a, a.yzwx, a.zwxy, a.wxyz);
    m[1].y = a.x * 3.0;
    m[2] = vec4(7.0);
    if (k > 0) m[3].x = 5.0;
    o = m[0] + vec4(m[3].x);
}
