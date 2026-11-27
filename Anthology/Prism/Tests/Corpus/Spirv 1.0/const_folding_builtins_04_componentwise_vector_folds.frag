#version 460 core

// The component-wise forms: a vector argument with a scalar edge, and a vector edge, both constant.

layout(location = 0) out vec4 FragColor;

const vec4 V = vec4(0.1, 0.4, 0.7, 1.3);
const vec4 W = vec4(0.5);

void main()
{
    vec4 mn = min(V, W);
    vec4 mx = max(V, 0.5);
    vec4 cl = clamp(V, 0.2, 0.8);
    vec4 clv = clamp(V, vec4(0.0), W);
    vec4 mixed = mix(V, W, 0.5);
    vec4 mixv = mix(V, W, vec4(0.25));
    vec4 st = step(W, V);
    vec4 ss = smoothstep(vec4(0.0), vec4(1.0), V);
    vec4 pw = pow(W, vec4(2.0));
    vec4 fr = fract(V);

    FragColor = mn + mx + cl + clv + mixed + mixv + st + ss + pw + fr;
}
