#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int k;
void main(){
    mediump float a[3] = float[3](v.x, v.y, v.z);
    mediump mat2 m = mat2(v);
    mediump vec4 w = v * 2.0;
    float x = a[k] + a[1];
    float y = m[1][k] + m[0].y;
    float z = w[k] + w.z;
    vec2 col = m[k];
    o = vec4(x, y, z, col.x + col.y);
}
