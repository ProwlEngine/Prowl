#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in ivec2 iv;
layout(location=2) flat in dvec2 dv;
layout(binding=0) uniform sampler2D tex;
mediump float gm = 0.5;
float gh = 2.0;
void main(){
    mediump float m = v.x;
    mediump int mi = iv.x;
    m += float(dv.x);
    m *= v.y;
    mediump int sh = mi << iv.y;
    int sh2 = iv.x >> mi;
    float t1 = v.z > 0.0 ? m : float(dv.y);
    float t2 = v.z > 0.0 ? sin(m) : cos(float(dv.y));
    mediump float t3 = v.w > 0.0 ? float(dv.x) : 1.0;
    mediump float b = max(float(dv.x), 0.0) + abs(m);
    mediump vec4 c = texture(tex, v.xy) * m;
    gm = gm * float(dv.y);
    o = vec4(m + float(sh + sh2), t1 + t2 + t3, b + gm + gh, 1.0) + c;
}
