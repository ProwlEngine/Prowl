#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    vec3 a;
    vec3 b;
    vec3 nref;
    float eta;
};

void main() {
    vec3 c = cross(a, b);
    vec3 r = reflect(a, normalize(nref));
    vec3 rf = refract(normalize(a), normalize(nref), eta);
    vec3 ff = faceforward(a, b, nref);
    fragColor = vec4(c + r + rf + ff, 1.0);
}
