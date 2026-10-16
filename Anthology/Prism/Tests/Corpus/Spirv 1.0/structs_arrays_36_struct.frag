#version 450
struct Light {
    vec3 position;
    float intensity;
    vec3 color;
};
layout(location = 0) in vec3 vWorldPos;
layout(location = 0) out vec4 oColor;
void main() {
    Light l;
    l.position = vec3(0.0, 5.0, 0.0);
    l.intensity = 2.0;
    l.color = vec3(1.0, 0.9, 0.8);
    float d = distance(l.position, vWorldPos);
    vec3 c = l.color * (l.intensity / (d * d));
    oColor = vec4(c, 1.0);
}
