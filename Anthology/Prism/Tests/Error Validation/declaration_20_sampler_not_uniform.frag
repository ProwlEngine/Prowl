#version 460

layout(location = 0) out vec4 fragColor;

sampler2D tex;

void main() {
    fragColor = texture(tex, vec2(0.5));
}
