#version 450
struct Light {
    vec4 positionRange;
    vec4 colorIntensity;
};
layout(set = 0, binding = 1) uniform Lights {
    Light lights[8];
    int count;
} scene;
layout(location = 0) in vec3 vWorldPos;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 acc = vec3(0.0);
    for (int i = 0; i < scene.count; ++i) {
        vec3 d = scene.lights[i].positionRange.xyz - vWorldPos;
        float att = 1.0 / (1.0 + dot(d, d));
        acc += scene.lights[i].colorIntensity.rgb * scene.lights[i].colorIntensity.a * att;
    }
    oColor = vec4(acc, 1.0);
}
