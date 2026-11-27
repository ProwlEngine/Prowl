#version 460
layout(location=0) out vec4 o;
void main() {
    vec3 v = vec3(1.0);
    float d = dot(v);
    o = vec4(d);
}
