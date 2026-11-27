#version 460
layout(location = 0) in vec4 a;
layout(location = 1) in vec4 b;
layout(location = 2) flat in ivec4 sel;
layout(location = 0) out vec4 c;
void main() {
    c = mix(a, b, bvec4(sel));
}
