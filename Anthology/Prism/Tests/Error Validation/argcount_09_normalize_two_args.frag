// normalize takes 1 argument but is called with 2
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec3 a;
layout(location=1) in vec3 b;
void main() {
    vec3 r = normalize(a, b);
    outColor = vec4(r, 1.0);
}
