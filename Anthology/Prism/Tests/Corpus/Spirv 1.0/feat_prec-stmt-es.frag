#version 310 es
precision mediump float;
layout(location=0) in vec4 v;
layout(location=0) out vec4 o;
layout(binding=0) uniform highp sampler2D tex;
void main(){
    precision highp float;
    vec4 a = v * 2.0;
    switch (int(v.x)) {
    case 0: {
        precision lowp float;
        precision lowp int;
        float l = a.x;
        int n = int(l) * 3;
        o.x = l + float(n);
        break;
    }
    default:
        o.x = a.y;
    }
    o.yzw = a.yzw + texture(tex, a.xy).xyz;
}
