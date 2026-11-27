#version 450
layout(location=0) in float x;
layout(location=1) flat in int k;
layout(location=0) out vec4 o;
float f(float p){ precision mediump float; float q = p * 2.0; return q; }
float g(float p){ return p + 1.0; }
void main(){
    float a = x;
    {
        precision mediump float;
        precision mediump int;
        float b = a * 2.0;
        int m = k + 1;
        o.x = b + float(m);
    }
    float c = a * 3.0;
    precision lowp float;
    float d = c + 1.0;
    for (int i = 0; i < 2; i++) { float e = d * float(i); o.w += e; }
    o.y = f(c) + g(d);
    o.z = d;
}
