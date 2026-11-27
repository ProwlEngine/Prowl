#version 450
layout(location=0) in vec4 a;
layout(location=1) flat in int k;
layout(location=0) out vec4 o;
void main(){
    mat3 m = mat3(a.xyz, a.yzx, a.zxy);
    for (int i = 0; i < k; i++) {
        m[0].x += 1.0;
        m[1] = m[1] * 2.0;
        if (i == 3) m[2].z = float(i);
        else m[2].y = a.w;
    }
    o = vec4(m[0], m[2].y);
}
