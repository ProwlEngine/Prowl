#version 450
layout(location = 0) in vec4 vIn;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 rgb = vIn.rgb;
    vec2 xy = vIn.xy;
    vec4 wzyx = vIn.wzyx;
    vec3 splat = vIn.zzz;
    oColor = vec4(rgb + splat, xy.x + wzyx.w);
}
