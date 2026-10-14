#version 450
layout(set = 0, binding = 0) uniform sampler2DShadow uShadow;
layout(location = 0) in vec4 vShadowCoord;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 coord = vShadowCoord.xyz / vShadowCoord.w;
    float lit = texture(uShadow, coord);
    oColor = vec4(vec3(lit), 1.0);
}
