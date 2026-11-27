#version 450
layout(set = 0, binding = 0) uniform Scene {
    vec4 lightPos;
    vec4 lightColor;
    vec4 viewPos;
} scene;
layout(set = 0, binding = 1) uniform sampler2D uAlbedo;
layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 albedo = texture(uAlbedo, vUV).rgb;
    vec3 N = normalize(vNormal);
    vec3 L = normalize(scene.lightPos.xyz - vWorldPos);
    vec3 V = normalize(scene.viewPos.xyz - vWorldPos);
    vec3 H = normalize(L + V);
    float diff = max(dot(N, L), 0.0);
    float spec = pow(max(dot(N, H), 0.0), 32.0);
    vec3 color = albedo * (0.1 + diff) + scene.lightColor.rgb * spec;
    oColor = vec4(color, 1.0);
}
