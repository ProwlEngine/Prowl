// reflect takes 2 arguments but is called with 1
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec3 a;
void main() {
    vec3 r = reflect(a);
    outColor = vec4(r, 1.0);
}
