#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in double dv;
void main(){
    mediump float m = v.x;
    float h = v.y;
    double d = dv * 2.0lf;
    float a = m * h;
    float b = m * float(d);
    float c = sin(m) + cos(h);
    float e = m + 1.0;
    float f = float(d) + 2.0;
    mediump float g = float(d);
    o = vec4(a, b, c, e) + vec4(f, g, m, h);
}
