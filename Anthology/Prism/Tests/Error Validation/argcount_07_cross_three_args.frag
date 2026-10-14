// cross takes 2 arguments but is called with 3
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec3 a;
layout(location=1) in vec3 b;
layout(location=2) in vec3 c;
void main() {
    vec3 r = cross(a, b, c);
    outColor = vec4(r, 1.0);
}
