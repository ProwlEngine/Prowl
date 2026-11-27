#version 450
layout(set = 0, binding = 0) uniform sampler2DShadow uShadowMap;
layout(location = 0) in vec4 vShadowCoord;
layout(location = 1) in vec3 vColor;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 coord = vShadowCoord.xyz / vShadowCoord.w;
    coord.xy = coord.xy * 0.5 + 0.5;
    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            vec3 offset = vec3(vec2(x, y) * texel, 0.0);
            shadow += texture(uShadowMap, coord + offset);
        }
    }
    shadow /= 9.0;
    oColor = vec4(vColor * shadow, 1.0);
}
