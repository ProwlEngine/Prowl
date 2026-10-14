#version 460
layout(location=0) out vec4 fragColor;

void main() {
    const vec4 c = vec4(0.1, 0.2, 0.3, 0.4);
    vec3 s = c.zyx;   // (0.3, 0.2, 0.1)
    fragColor = vec4(s, c.w);
}
