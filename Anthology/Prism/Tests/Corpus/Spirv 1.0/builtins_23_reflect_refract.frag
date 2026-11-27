#version 450
layout(location = 0) in vec3 vI;
layout(location = 1) in vec3 vN;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 n = normalize(vN);
    vec3 i = normalize(vI);
    vec3 r = reflect(i, n);
    vec3 rr = refract(i, n, 1.33);
    vec3 ff = faceforward(n, i, n);
    oColor = vec4(r + rr + ff, 1.0);
}
