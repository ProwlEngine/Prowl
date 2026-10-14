#version 450
// Left side write mask repeats the x component which is not allowed
layout(location=0) out vec4 outColor;
void main() {
    vec2 v = vec2(0.0);
    v.xx = vec2(1.0, 2.0);
    outColor = vec4(v, 0.0, 1.0);
}
