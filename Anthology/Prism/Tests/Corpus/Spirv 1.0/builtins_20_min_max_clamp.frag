#version 450
layout(location = 0) in vec3 vX;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 mn = min(vX, vec3(0.5));
    vec3 mx = max(vX, vec3(0.2));
    vec3 cl = clamp(vX, 0.0, 1.0);
    oColor = vec4(mn + mx + cl, 1.0);
}
