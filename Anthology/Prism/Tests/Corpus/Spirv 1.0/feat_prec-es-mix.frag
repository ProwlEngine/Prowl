#version 310 es
precision highp float;
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int n;
layout(binding=0) uniform mediump sampler2D t;
void main(){
    mediump vec2 uv = v.xy;
    lowp float l = v.z;
    vec4 s = texture(t, uv);
    float a = l * float(n) + s.x;
    mediump float c = clamp(a, 0.0, 1.0);
    o = vec4(a, c, float(gl_FrontFacing), l + 1.0) * s;
}
