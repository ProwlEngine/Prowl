#version 460
layout(set = 0, binding = 0) uniform sampler2DShadow shadowTex;
layout(location = 0) in vec3 coord;
layout(location = 0) out vec4 fragColor;
void main() {
    float d = texture(shadowTex, coord, 0.5);
    fragColor = vec4(d);
}
