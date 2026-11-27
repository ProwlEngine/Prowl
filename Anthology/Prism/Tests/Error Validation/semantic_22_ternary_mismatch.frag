#version 460
layout(location=0) out vec4 o;
layout(location=0) in float cond;
void main() {
    vec3 v = (cond > 0.0) ? vec3(0.0) : mat2(1.0);
    o = vec4(v, 1.0);
}
