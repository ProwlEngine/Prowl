#version 450
layout(location = 0) out vec4 oColor;
void main() {
    vec2 a = vec2(1.0);
    vec3 b = vec3(a, 2.0);
    vec4 c = vec4(b, 3.0);
    vec4 d = vec4(a, a);
    oColor = c + d;
}
